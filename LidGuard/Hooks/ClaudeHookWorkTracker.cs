using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LidGuard.Hooks;

internal static class ClaudeHookWorkTracker
{
    private const int LockRetryCount = 20;
    private const int LockRetryDelayMilliseconds = 25;
    private const int RecentTranscriptLineLimit = 4096;
    private const int RecentTranscriptByteLimit = 4_194_304;
    private const string ActiveBackgroundTasksPropertyName = "activeBackgroundTasks";
    private const string ActiveSubagentsPropertyName = "activeSubagents";
    private const string StateDirectoryName = "claude-hook-work-state";

    public static void RecordToolUseEvent(ClaudeHookInput hookInput, string sessionIdentifier)
    {
        ArgumentNullException.ThrowIfNull(hookInput);

        if (TryCreateBackgroundWorkItem(hookInput, out var backgroundWorkItem))
        {
            UpdateSessionState(sessionIdentifier, sessionWorkState => sessionWorkState.UpsertBackgroundTask(backgroundWorkItem));
            return;
        }

        if (TryGetStoppedTaskIdentifier(hookInput, out var stoppedTaskIdentifier)) UpdateSessionState(sessionIdentifier, sessionWorkState => sessionWorkState.RemoveBackgroundTask(stoppedTaskIdentifier, string.Empty));
    }

    public static void RecordSubagentStarted(ClaudeHookInput hookInput, string sessionIdentifier)
    {
        ArgumentNullException.ThrowIfNull(hookInput);

        if (string.IsNullOrWhiteSpace(hookInput.AgentIdentifier)) return;

        var subagentWorkItem = new ClaudeHookSubagentWorkItem(
            hookInput.AgentIdentifier.Trim(),
            hookInput.AgentType.Trim(),
            hookInput.AgentTranscriptPath.Trim(),
            DateTimeOffset.UtcNow);
        UpdateSessionState(sessionIdentifier, sessionWorkState => sessionWorkState.UpsertSubagent(subagentWorkItem));
    }

    public static void RecordSubagentStopped(ClaudeHookInput hookInput, string sessionIdentifier)
    {
        ArgumentNullException.ThrowIfNull(hookInput);

        if (string.IsNullOrWhiteSpace(hookInput.AgentIdentifier)) return;
        UpdateSessionState(sessionIdentifier, sessionWorkState => sessionWorkState.RemoveSubagent(hookInput.AgentIdentifier));
    }

    public static void RecordTaskCreated(ClaudeHookInput hookInput, string sessionIdentifier)
    {
        ArgumentNullException.ThrowIfNull(hookInput);

        if (string.IsNullOrWhiteSpace(hookInput.TaskIdentifier)) return;

        var backgroundWorkItem = new ClaudeHookBackgroundWorkItem(
            string.Empty,
            hookInput.TaskIdentifier.Trim(),
            ClaudeHookEventNames.TaskCreated,
            CreateTaskSummary(hookInput),
            DateTimeOffset.UtcNow);
        UpdateSessionState(sessionIdentifier, sessionWorkState => sessionWorkState.UpsertBackgroundTask(backgroundWorkItem));
    }

    public static void RecordTaskCompleted(ClaudeHookInput hookInput, string sessionIdentifier)
    {
        ArgumentNullException.ThrowIfNull(hookInput);

        if (string.IsNullOrWhiteSpace(hookInput.TaskIdentifier)) return;
        UpdateSessionState(sessionIdentifier, sessionWorkState => sessionWorkState.RemoveBackgroundTask(hookInput.TaskIdentifier, string.Empty));
    }

    public static bool TryRecordTaskNotification(ClaudeHookInput hookInput, string sessionIdentifier)
    {
        ArgumentNullException.ThrowIfNull(hookInput);

        if (!TryParseTaskNotification(hookInput.Prompt, out var taskNotification)) return false;
        if (!taskNotification.IsTerminal) return true;

        UpdateSessionState(
            sessionIdentifier,
            sessionWorkState => sessionWorkState.RemoveBackgroundTask(
                taskNotification.TaskIdentifier,
                taskNotification.ToolUseIdentifier));
        return true;
    }

    public static bool TryCreatePendingWorkReason(ClaudeHookInput hookInput, string sessionIdentifier, out string reason)
    {
        ArgumentNullException.ThrowIfNull(hookInput);

        reason = string.Empty;
        SynchronizeBackgroundTasksFromTranscript(hookInput.TranscriptPath, sessionIdentifier);

        var sessionWorkState = ReadSessionState(sessionIdentifier);
        var pendingWorkSnapshot = new ClaudeHookPendingWorkSnapshot(
            [.. sessionWorkState.ActiveSubagents],
            [.. sessionWorkState.ActiveBackgroundTasks]);
        if (!pendingWorkSnapshot.HasPendingWork) return false;

        reason = pendingWorkSnapshot.CreatePendingWorkReason(hookInput.StopHookActive);
        ClaudeHookEventLog.AppendMessage($"LidGuard Claude hook deferred Stop because pending work remains: {pendingWorkSnapshot.CreateLogSummary()}");
        return true;
    }

    public static void RecordDeferredStop(
        string sessionIdentifier,
        bool isProviderSessionEnd,
        string sessionEndReason,
        string pendingProviderWorkReason)
    {
        UpdateSessionState(
            sessionIdentifier,
            sessionWorkState => sessionWorkState.DeferredStop = new ClaudeHookDeferredStop(
                isProviderSessionEnd,
                sessionEndReason,
                pendingProviderWorkReason,
                DateTimeOffset.UtcNow));
    }

    public static void ClearSessionState(string sessionIdentifier)
    {
        if (string.IsNullOrWhiteSpace(sessionIdentifier)) return;

        try
        {
            var stateFilePath = GetStateFilePath(sessionIdentifier);
            var lockFilePath = GetLockFilePath(stateFilePath);
            EnsureStateDirectory(stateFilePath);
            using var lockFileStream = OpenLockFile(lockFilePath);
            if (File.Exists(stateFilePath)) File.Delete(stateFilePath);
        }
        catch (Exception exception) when (IsStateFileException(exception))
        {
            ClaudeHookEventLog.AppendMessage($"LidGuard Claude hook could not clear work state: {exception.Message}");
        }
    }

    private static void SynchronizeBackgroundTasksFromTranscript(string transcriptPath, string sessionIdentifier)
    {
        if (string.IsNullOrWhiteSpace(transcriptPath)) return;

        var transcriptWorkSnapshot = ClaudeHookTranscriptWorkScanner.Scan(transcriptPath);
        if (transcriptWorkSnapshot.IsEmpty) return;

        UpdateSessionState(sessionIdentifier, sessionWorkState => sessionWorkState.ApplyTranscriptWorkSnapshot(transcriptWorkSnapshot));
    }

    private static bool TryCreateBackgroundWorkItem(ClaudeHookInput hookInput, out ClaudeHookBackgroundWorkItem backgroundWorkItem)
    {
        backgroundWorkItem = default;
        if (!hookInput.HookEventName.Trim().Equals(ClaudeHookEventNames.PostToolUse, StringComparison.Ordinal)) return false;
        if (!IsBackgroundWorkStart(hookInput.ToolName, hookInput.ToolInput)) return false;

        var toolUseIdentifier = hookInput.ToolUseIdentifier.Trim();
        var taskIdentifier = ExtractTaskIdentifier(hookInput.ToolResponse);
        if (string.IsNullOrWhiteSpace(toolUseIdentifier) && string.IsNullOrWhiteSpace(taskIdentifier)) return false;

        backgroundWorkItem = new ClaudeHookBackgroundWorkItem(
            toolUseIdentifier,
            taskIdentifier,
            hookInput.ToolName.Trim(),
            CreateBackgroundWorkSummary(hookInput.ToolName, hookInput.ToolInput, taskIdentifier, toolUseIdentifier),
            DateTimeOffset.UtcNow);
        return true;
    }

    private static bool TryGetStoppedTaskIdentifier(ClaudeHookInput hookInput, out string taskIdentifier)
    {
        taskIdentifier = string.Empty;
        var isToolUseCompletionEvent = hookInput.HookEventName.Trim().Equals(ClaudeHookEventNames.PostToolUse, StringComparison.Ordinal)
            || hookInput.HookEventName.Trim().Equals(ClaudeHookEventNames.PostToolUseFailure, StringComparison.Ordinal);
        if (!isToolUseCompletionEvent) return false;

        if (!hookInput.ToolName.Trim().Equals(ClaudeHookEventNames.TaskStopToolName, StringComparison.Ordinal)) return false;
        return TryGetStringProperty(hookInput.ToolInput, "task_id", out taskIdentifier);
    }

    private static void UpdateSessionState(string sessionIdentifier, Action<ClaudeHookSessionWorkState> updateState)
    {
        if (string.IsNullOrWhiteSpace(sessionIdentifier)) return;

        try
        {
            var stateFilePath = GetStateFilePath(sessionIdentifier);
            var lockFilePath = GetLockFilePath(stateFilePath);
            EnsureStateDirectory(stateFilePath);
            using var lockFileStream = OpenLockFile(lockFilePath);
            var sessionWorkState = ReadSessionStateWithoutLock(stateFilePath);
            updateState(sessionWorkState);
            WriteSessionStateWithoutLock(stateFilePath, sessionIdentifier, sessionWorkState);
        }
        catch (Exception exception) when (IsStateFileException(exception))
        {
            ClaudeHookEventLog.AppendMessage($"LidGuard Claude hook could not update work state: {exception.Message}");
        }
    }

    private static ClaudeHookSessionWorkState ReadSessionState(string sessionIdentifier)
    {
        if (string.IsNullOrWhiteSpace(sessionIdentifier)) return new ClaudeHookSessionWorkState();

        try
        {
            var stateFilePath = GetStateFilePath(sessionIdentifier);
            var lockFilePath = GetLockFilePath(stateFilePath);
            EnsureStateDirectory(stateFilePath);
            using var lockFileStream = OpenLockFile(lockFilePath);
            return ReadSessionStateWithoutLock(stateFilePath);
        }
        catch (Exception exception) when (IsStateFileException(exception))
        {
            ClaudeHookEventLog.AppendMessage($"LidGuard Claude hook could not read work state: {exception.Message}");
            return new ClaudeHookSessionWorkState();
        }
    }

    private static ClaudeHookSessionWorkState ReadSessionStateWithoutLock(string stateFilePath)
    {
        if (!File.Exists(stateFilePath)) return new ClaudeHookSessionWorkState();

        try
        {
            var rootNode = JsonNode.Parse(File.ReadAllText(stateFilePath));
            if (rootNode is not JsonObject rootObject) return new ClaudeHookSessionWorkState();

            var sessionWorkState = new ClaudeHookSessionWorkState();
            if (rootObject["deferredStop"] is JsonObject deferredStopObject)
            {
                sessionWorkState.DeferredStop = new ClaudeHookDeferredStop(
                    GetBooleanProperty(deferredStopObject, "isProviderSessionEnd"),
                    GetStringProperty(deferredStopObject, "sessionEndReason"),
                    GetStringProperty(deferredStopObject, "pendingProviderWorkReason"),
                    GetDateTimeOffsetProperty(deferredStopObject, "deferredAt"));
            }

            if (rootObject[ActiveSubagentsPropertyName] is JsonArray activeSubagents)
            {
                foreach (var activeSubagentNode in activeSubagents)
                {
                    if (activeSubagentNode is not JsonObject activeSubagentObject) continue;

                    var agentIdentifier = GetStringProperty(activeSubagentObject, "agentIdentifier");
                    if (string.IsNullOrWhiteSpace(agentIdentifier)) continue;

                    sessionWorkState.UpsertSubagent(new ClaudeHookSubagentWorkItem(
                        agentIdentifier,
                        GetStringProperty(activeSubagentObject, "agentType"),
                        GetStringProperty(activeSubagentObject, "agentTranscriptPath"),
                        GetDateTimeOffsetProperty(activeSubagentObject, "startedAt")));
                }
            }

            if (rootObject[ActiveBackgroundTasksPropertyName] is JsonArray activeBackgroundTasks)
            {
                foreach (var activeBackgroundTaskNode in activeBackgroundTasks)
                {
                    if (activeBackgroundTaskNode is not JsonObject activeBackgroundTaskObject) continue;

                    var toolUseIdentifier = GetStringProperty(activeBackgroundTaskObject, "toolUseIdentifier");
                    var taskIdentifier = GetStringProperty(activeBackgroundTaskObject, "taskIdentifier");
                    if (string.IsNullOrWhiteSpace(toolUseIdentifier) && string.IsNullOrWhiteSpace(taskIdentifier)) continue;

                    sessionWorkState.UpsertBackgroundTask(new ClaudeHookBackgroundWorkItem(
                        toolUseIdentifier,
                        taskIdentifier,
                        GetStringProperty(activeBackgroundTaskObject, "toolName"),
                        GetStringProperty(activeBackgroundTaskObject, "summary"),
                        GetDateTimeOffsetProperty(activeBackgroundTaskObject, "startedAt")));
                }
            }

            return sessionWorkState;
        }
        catch (JsonException) { return new ClaudeHookSessionWorkState(); }
    }

    private static void WriteSessionStateWithoutLock(
        string stateFilePath,
        string sessionIdentifier,
        ClaudeHookSessionWorkState sessionWorkState)
    {
        if (!sessionWorkState.ShouldPersist)
        {
            if (File.Exists(stateFilePath)) File.Delete(stateFilePath);
            return;
        }

        var rootObject = new JsonObject
        {
            ["sessionIdentifier"] = sessionIdentifier,
            ["updatedAt"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            [ActiveSubagentsPropertyName] = CreateSubagentsArray(sessionWorkState.ActiveSubagents),
            [ActiveBackgroundTasksPropertyName] = CreateBackgroundTasksArray(sessionWorkState.ActiveBackgroundTasks)
        };
        if (sessionWorkState.HasDeferredStop)
        {
            rootObject["deferredStop"] = new JsonObject
            {
                ["isProviderSessionEnd"] = sessionWorkState.DeferredStop.IsProviderSessionEnd,
                ["sessionEndReason"] = sessionWorkState.DeferredStop.SessionEndReason,
                ["pendingProviderWorkReason"] = sessionWorkState.DeferredStop.PendingProviderWorkReason,
                ["deferredAt"] = sessionWorkState.DeferredStop.DeferredAt.ToString("O", CultureInfo.InvariantCulture)
            };
        }

        File.WriteAllText(stateFilePath, rootObject.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }

    private static JsonArray CreateSubagentsArray(IEnumerable<ClaudeHookSubagentWorkItem> subagentWorkItems)
    {
        var subagentsArray = new JsonArray();
        foreach (var subagentWorkItem in subagentWorkItems)
        {
            subagentsArray.Add((JsonNode)new JsonObject
            {
                ["agentIdentifier"] = subagentWorkItem.AgentIdentifier,
                ["agentType"] = subagentWorkItem.AgentType,
                ["agentTranscriptPath"] = subagentWorkItem.AgentTranscriptPath,
                ["startedAt"] = subagentWorkItem.StartedAt.ToString("O", CultureInfo.InvariantCulture)
            });
        }

        return subagentsArray;
    }

    private static JsonArray CreateBackgroundTasksArray(IEnumerable<ClaudeHookBackgroundWorkItem> backgroundWorkItems)
    {
        var backgroundTasksArray = new JsonArray();
        foreach (var backgroundWorkItem in backgroundWorkItems)
        {
            backgroundTasksArray.Add((JsonNode)new JsonObject
            {
                ["toolUseIdentifier"] = backgroundWorkItem.ToolUseIdentifier,
                ["taskIdentifier"] = backgroundWorkItem.TaskIdentifier,
                ["toolName"] = backgroundWorkItem.ToolName,
                ["summary"] = backgroundWorkItem.Summary,
                ["startedAt"] = backgroundWorkItem.StartedAt.ToString("O", CultureInfo.InvariantCulture)
            });
        }

        return backgroundTasksArray;
    }

    private static bool IsBackgroundWorkStart(string toolName, JsonElement toolInput)
    {
        var normalizedToolName = toolName.Trim();
        if (normalizedToolName.Equals(ClaudeHookEventNames.MonitorToolName, StringComparison.Ordinal)) return true;
        var isBackgroundCapableTool = normalizedToolName.Equals(ClaudeHookEventNames.BashToolName, StringComparison.Ordinal)
            || normalizedToolName.Equals(ClaudeHookEventNames.PowerShellToolName, StringComparison.Ordinal)
            || normalizedToolName.Equals(ClaudeHookEventNames.AgentToolName, StringComparison.Ordinal)
            || normalizedToolName.Equals(ClaudeHookEventNames.TaskToolName, StringComparison.Ordinal);
        if (!isBackgroundCapableTool) return false;

        return TryGetBooleanProperty(toolInput, "run_in_background", out var runInBackground) && runInBackground;
    }

    private static string CreateBackgroundWorkSummary(
        string toolName,
        JsonElement toolInput,
        string taskIdentifier,
        string toolUseIdentifier)
    {
        var detail = string.Empty;
        var hasBackgroundWorkDetail = TryGetStringProperty(toolInput, "description", out detail)
            || TryGetStringProperty(toolInput, "command", out detail)
            || TryGetStringProperty(toolInput, "subagent_type", out detail)
            || TryGetStringProperty(toolInput, "prompt", out detail);
        if (!hasBackgroundWorkDetail) detail = string.Empty;

        if (detail.Length > 80) detail = detail[..77] + "...";

        var identifier = string.IsNullOrWhiteSpace(taskIdentifier) ? toolUseIdentifier : taskIdentifier;
        if (string.IsNullOrWhiteSpace(detail)) return $"{toolName} {identifier}".Trim();
        return $"{toolName} {identifier}: {detail}".Trim();
    }

    private static string CreateTaskSummary(ClaudeHookInput hookInput)
    {
        var taskDescription = hookInput.TaskSubject.Trim();
        if (string.IsNullOrWhiteSpace(taskDescription)) taskDescription = hookInput.TaskDescription.Trim();
        if (string.IsNullOrWhiteSpace(taskDescription)) taskDescription = hookInput.TeammateName.Trim();
        if (taskDescription.Length > 80) taskDescription = taskDescription[..77] + "...";

        if (string.IsNullOrWhiteSpace(taskDescription)) return $"{ClaudeHookEventNames.TaskCreated} {hookInput.TaskIdentifier}".Trim();
        return $"{ClaudeHookEventNames.TaskCreated} {hookInput.TaskIdentifier}: {taskDescription}".Trim();
    }

    private static bool TryParseTaskNotification(string content, out ClaudeHookTaskNotification taskNotification)
    {
        taskNotification = default;
        if (string.IsNullOrWhiteSpace(content)) return false;

        var taskNotificationStartIndex = content.IndexOf("<task-notification", StringComparison.Ordinal);
        if (taskNotificationStartIndex < 0) return false;

        var taskNotificationStartTagEndIndex = content.IndexOf('>', taskNotificationStartIndex);
        if (taskNotificationStartTagEndIndex < 0) return false;

        var taskNotificationEndIndex = content.IndexOf("</task-notification>", taskNotificationStartTagEndIndex, StringComparison.Ordinal);
        if (taskNotificationEndIndex < 0) return false;

        var taskNotificationContent = content[(taskNotificationStartTagEndIndex + 1)..taskNotificationEndIndex];
        taskNotification = new ClaudeHookTaskNotification(
            ExtractTagText(taskNotificationContent, "task-id"),
            ExtractTagText(taskNotificationContent, "tool-use-id"),
            ExtractTagText(taskNotificationContent, "status"));
        return true;
    }

    private static string ExtractTagText(string content, string tagName)
    {
        var openingTag = $"<{tagName}>";
        var closingTag = $"</{tagName}>";
        var openingTagIndex = content.IndexOf(openingTag, StringComparison.Ordinal);
        if (openingTagIndex < 0) return string.Empty;

        var valueStartIndex = openingTagIndex + openingTag.Length;
        var closingTagIndex = content.IndexOf(closingTag, valueStartIndex, StringComparison.Ordinal);
        if (closingTagIndex < 0) return string.Empty;

        return content[valueStartIndex..closingTagIndex].Trim();
    }

    private static bool IsTerminalTaskStatus(string taskStatus)
        => taskStatus.Equals("completed", StringComparison.Ordinal)
            || taskStatus.Equals("failed", StringComparison.Ordinal)
            || taskStatus.Equals("stopped", StringComparison.Ordinal);

    private static string ExtractTaskIdentifier(JsonElement element)
    {
        if (TryFindStringProperty(element, "task_id", out var taskIdentifier)) return taskIdentifier;
        if (TryFindStringProperty(element, "taskId", out taskIdentifier)) return taskIdentifier;
        if (TryFindStringProperty(element, "background_task_id", out taskIdentifier)) return taskIdentifier;
        if (TryFindStringProperty(element, "backgroundTaskId", out taskIdentifier)) return taskIdentifier;
        return string.Empty;
    }

    private static bool TryFindStringProperty(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals(propertyName) && property.Value.ValueKind == JsonValueKind.String)
                {
                    value = property.Value.GetString() ?? string.Empty;
                    return true;
                }

                if (TryFindStringProperty(property.Value, propertyName, out value)) return true;
            }
        }

        if (element.ValueKind != JsonValueKind.Array) return false;
        foreach (var itemElement in element.EnumerateArray()) if (TryFindStringProperty(itemElement, propertyName, out value)) return true;

        return false;
    }

    private static bool TryGetBooleanProperty(JsonElement element, string propertyName, out bool value)
        => HookJsonPropertyReader.TryGetBooleanProperty(element, propertyName, out value);

    private static bool TryGetStringProperty(JsonElement element, string propertyName, out string value)
        => HookJsonPropertyReader.TryGetNonWhiteSpaceStringProperty(element, propertyName, out value);

    private static string GetStringProperty(JsonObject jsonObject, string propertyName)
        => HookJsonPropertyReader.GetStringProperty(jsonObject, propertyName);

    private static DateTimeOffset GetDateTimeOffsetProperty(JsonObject jsonObject, string propertyName)
    {
        var value = GetStringProperty(jsonObject, propertyName);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTimeOffset)
            ? dateTimeOffset
            : DateTimeOffset.MinValue;
    }

    private static bool GetBooleanProperty(JsonObject jsonObject, string propertyName)
        => HookJsonPropertyReader.GetBooleanProperty(jsonObject, propertyName);

    private static string GetStateFilePath(string sessionIdentifier)
    {
        var localApplicationDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationDataPath)) localApplicationDataPath = Path.GetTempPath();

        var sessionIdentifierHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionIdentifier))).ToLowerInvariant();
        return Path.Combine(localApplicationDataPath, "LidGuard", StateDirectoryName, $"{sessionIdentifierHash}.json");
    }

    private static string GetLockFilePath(string stateFilePath) => stateFilePath + ".lock";

    private static void EnsureStateDirectory(string stateFilePath)
    {
        var stateDirectoryPath = Path.GetDirectoryName(stateFilePath);
        if (!string.IsNullOrWhiteSpace(stateDirectoryPath)) Directory.CreateDirectory(stateDirectoryPath);
    }

    private static FileStream OpenLockFile(string lockFilePath)
    {
        for (var attemptIndex = 0; attemptIndex < LockRetryCount - 1; attemptIndex++)
        {
            try { return new FileStream(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { Thread.Sleep(LockRetryDelayMilliseconds); }
        }

        return new FileStream(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private static bool IsStateFileException(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or DirectoryNotFoundException
            or PathTooLongException
            or NotSupportedException;

    private sealed class ClaudeHookSessionWorkState
    {
        public List<ClaudeHookSubagentWorkItem> ActiveSubagents { get; } = [];

        public List<ClaudeHookBackgroundWorkItem> ActiveBackgroundTasks { get; } = [];

        public ClaudeHookDeferredStop DeferredStop { get; set; }

        public bool HasPendingWork => ActiveSubagents.Count > 0 || ActiveBackgroundTasks.Count > 0;

        public bool HasDeferredStop => DeferredStop is not null;

        public bool ShouldPersist => HasPendingWork || HasDeferredStop;

        public void UpsertSubagent(ClaudeHookSubagentWorkItem subagentWorkItem)
        {
            ActiveSubagents.RemoveAll(existingSubagentWorkItem => existingSubagentWorkItem.AgentIdentifier.Equals(subagentWorkItem.AgentIdentifier, StringComparison.Ordinal));
            ActiveSubagents.Add(subagentWorkItem);
        }

        public void RemoveSubagent(string agentIdentifier)
            => ActiveSubagents.RemoveAll(subagentWorkItem => subagentWorkItem.AgentIdentifier.Equals(agentIdentifier.Trim(), StringComparison.Ordinal));

        public void UpsertBackgroundTask(ClaudeHookBackgroundWorkItem backgroundWorkItem)
        {
            ActiveBackgroundTasks.RemoveAll(existingBackgroundWorkItem => WorkItemsReferToSameTask(existingBackgroundWorkItem, backgroundWorkItem));
            ActiveBackgroundTasks.Add(backgroundWorkItem);
        }

        public void RemoveBackgroundTask(string taskIdentifier, string toolUseIdentifier)
        {
            ActiveBackgroundTasks.RemoveAll(backgroundWorkItem =>
                !string.IsNullOrWhiteSpace(taskIdentifier)
                    && backgroundWorkItem.TaskIdentifier.Equals(taskIdentifier.Trim(), StringComparison.Ordinal)
                || !string.IsNullOrWhiteSpace(toolUseIdentifier)
                    && backgroundWorkItem.ToolUseIdentifier.Equals(toolUseIdentifier.Trim(), StringComparison.Ordinal));
        }

        public void ApplyTranscriptWorkSnapshot(ClaudeHookTranscriptWorkSnapshot transcriptWorkSnapshot)
        {
            foreach (var completedTaskIdentifier in transcriptWorkSnapshot.CompletedTaskIdentifiers) RemoveBackgroundTask(completedTaskIdentifier, string.Empty);
            foreach (var completedToolUseIdentifier in transcriptWorkSnapshot.CompletedToolUseIdentifiers) RemoveBackgroundTask(string.Empty, completedToolUseIdentifier);
            foreach (var backgroundWorkItem in transcriptWorkSnapshot.StartedBackgroundTasks)
            {
                if (transcriptWorkSnapshot.IsCompleted(backgroundWorkItem)) continue;
                UpsertBackgroundTask(backgroundWorkItem);
            }
        }

        private static bool WorkItemsReferToSameTask(
            ClaudeHookBackgroundWorkItem firstBackgroundWorkItem,
            ClaudeHookBackgroundWorkItem secondBackgroundWorkItem)
        {
            var hasMatchingToolUseIdentifier = !string.IsNullOrWhiteSpace(firstBackgroundWorkItem.ToolUseIdentifier)
                && firstBackgroundWorkItem.ToolUseIdentifier.Equals(secondBackgroundWorkItem.ToolUseIdentifier, StringComparison.Ordinal);
            if (hasMatchingToolUseIdentifier) return true;

            return !string.IsNullOrWhiteSpace(firstBackgroundWorkItem.TaskIdentifier)
                && firstBackgroundWorkItem.TaskIdentifier.Equals(secondBackgroundWorkItem.TaskIdentifier, StringComparison.Ordinal);
        }
    }

    private sealed class ClaudeHookTranscriptWorkSnapshot
    {
        public List<ClaudeHookBackgroundWorkItem> StartedBackgroundTasks { get; } = [];

        public HashSet<string> CompletedTaskIdentifiers { get; } = new(StringComparer.Ordinal);

        public HashSet<string> CompletedToolUseIdentifiers { get; } = new(StringComparer.Ordinal);

        public bool IsEmpty => StartedBackgroundTasks.Count == 0 && CompletedTaskIdentifiers.Count == 0 && CompletedToolUseIdentifiers.Count == 0;

        public void AddStartedBackgroundTask(ClaudeHookBackgroundWorkItem backgroundWorkItem)
        {
            if (StartedBackgroundTasks.Any(existingBackgroundWorkItem => WorkItemsReferToSameTask(existingBackgroundWorkItem, backgroundWorkItem))) return;
            StartedBackgroundTasks.Add(backgroundWorkItem);
        }

        public void AddTaskNotification(ClaudeHookTaskNotification taskNotification)
        {
            if (!taskNotification.IsTerminal) return;
            if (!string.IsNullOrWhiteSpace(taskNotification.TaskIdentifier)) CompletedTaskIdentifiers.Add(taskNotification.TaskIdentifier);
            if (!string.IsNullOrWhiteSpace(taskNotification.ToolUseIdentifier)) CompletedToolUseIdentifiers.Add(taskNotification.ToolUseIdentifier);
        }

        public bool IsCompleted(ClaudeHookBackgroundWorkItem backgroundWorkItem)
            => !string.IsNullOrWhiteSpace(backgroundWorkItem.TaskIdentifier)
                && CompletedTaskIdentifiers.Contains(backgroundWorkItem.TaskIdentifier)
            || !string.IsNullOrWhiteSpace(backgroundWorkItem.ToolUseIdentifier)
                && CompletedToolUseIdentifiers.Contains(backgroundWorkItem.ToolUseIdentifier);

        private static bool WorkItemsReferToSameTask(
            ClaudeHookBackgroundWorkItem firstBackgroundWorkItem,
            ClaudeHookBackgroundWorkItem secondBackgroundWorkItem)
        {
            var hasMatchingToolUseIdentifier = !string.IsNullOrWhiteSpace(firstBackgroundWorkItem.ToolUseIdentifier)
                && firstBackgroundWorkItem.ToolUseIdentifier.Equals(secondBackgroundWorkItem.ToolUseIdentifier, StringComparison.Ordinal);
            if (hasMatchingToolUseIdentifier) return true;

            return !string.IsNullOrWhiteSpace(firstBackgroundWorkItem.TaskIdentifier)
                && firstBackgroundWorkItem.TaskIdentifier.Equals(secondBackgroundWorkItem.TaskIdentifier, StringComparison.Ordinal);
        }
    }

    private static class ClaudeHookTranscriptWorkScanner
    {
        public static ClaudeHookTranscriptWorkSnapshot Scan(string transcriptPath)
        {
            var transcriptWorkSnapshot = new ClaudeHookTranscriptWorkSnapshot();
            foreach (var transcriptLine in ReadRecentTranscriptLines(transcriptPath))
            {
                try
                {
                    using var document = JsonDocument.Parse(transcriptLine);
                    InspectElement(document.RootElement, transcriptWorkSnapshot);
                }
                catch (JsonException) { }
            }

            return transcriptWorkSnapshot;
        }

        private static void InspectElement(JsonElement element, ClaudeHookTranscriptWorkSnapshot transcriptWorkSnapshot)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (IsQueueOperationElement(element)) return;

                InspectTaskNotification(element, transcriptWorkSnapshot);
                InspectToolUse(element, transcriptWorkSnapshot);
                foreach (var property in element.EnumerateObject()) InspectElement(property.Value, transcriptWorkSnapshot);
                return;
            }

            if (element.ValueKind == JsonValueKind.String)
            {
                InspectTaskNotificationText(element.GetString(), transcriptWorkSnapshot);
                return;
            }

            if (element.ValueKind != JsonValueKind.Array) return;
            foreach (var itemElement in element.EnumerateArray()) InspectElement(itemElement, transcriptWorkSnapshot);
        }

        private static bool IsQueueOperationElement(JsonElement element)
        {
            if (!TryGetStringProperty(element, "type", out var type)) return false;
            return type.Equals("queue-operation", StringComparison.Ordinal);
        }

        private static void InspectTaskNotification(JsonElement element, ClaudeHookTranscriptWorkSnapshot transcriptWorkSnapshot)
        {
            if (TryGetStringProperty(element, "content", out var content)) InspectTaskNotificationText(content, transcriptWorkSnapshot);
            if (!TryGetStringProperty(element, "type", out var type) || !type.Equals("system", StringComparison.Ordinal)) return;
            if (!TryGetStringProperty(element, "subtype", out var subtype) || !subtype.Equals("task_notification", StringComparison.Ordinal)) return;
            if (!TryGetStringProperty(element, "status", out var taskStatus)) return;
            if (!IsTerminalTaskStatus(taskStatus)) return;

            if (TryGetStringProperty(element, "task_id", out var taskIdentifier)) transcriptWorkSnapshot.CompletedTaskIdentifiers.Add(taskIdentifier);
            if (TryGetStringProperty(element, "tool_use_id", out var toolUseIdentifier)) transcriptWorkSnapshot.CompletedToolUseIdentifiers.Add(toolUseIdentifier);
        }

        private static void InspectTaskNotificationText(string content, ClaudeHookTranscriptWorkSnapshot transcriptWorkSnapshot)
        {
            if (TryParseTaskNotification(content, out var taskNotification)) transcriptWorkSnapshot.AddTaskNotification(taskNotification);
        }

        private static void InspectToolUse(JsonElement element, ClaudeHookTranscriptWorkSnapshot transcriptWorkSnapshot)
        {
            if (!TryGetToolNameAndInput(element, out var toolName, out var toolInput)) return;

            if (toolName.Equals(ClaudeHookEventNames.TaskStopToolName, StringComparison.Ordinal))
            {
                if (TryGetStringProperty(toolInput, "task_id", out var stoppedTaskIdentifier)) transcriptWorkSnapshot.CompletedTaskIdentifiers.Add(stoppedTaskIdentifier);
                return;
            }

            if (!IsBackgroundWorkStart(toolName, toolInput)) return;

            var toolUseIdentifier = GetToolUseIdentifier(element);
            var taskIdentifier = GetTaskIdentifierFromToolUseElement(element);
            if (string.IsNullOrWhiteSpace(toolUseIdentifier) && string.IsNullOrWhiteSpace(taskIdentifier)) return;

            transcriptWorkSnapshot.AddStartedBackgroundTask(new ClaudeHookBackgroundWorkItem(
                toolUseIdentifier,
                taskIdentifier,
                toolName,
                CreateBackgroundWorkSummary(toolName, toolInput, taskIdentifier, toolUseIdentifier),
                DateTimeOffset.UtcNow));
        }

        private static bool TryGetToolNameAndInput(JsonElement element, out string toolName, out JsonElement toolInput)
        {
            toolName = string.Empty;
            toolInput = default;

            var hasHookToolNameAndInput = TryGetStringProperty(element, "tool_name", out toolName)
                && element.TryGetProperty("tool_input", out toolInput);
            if (hasHookToolNameAndInput) return true;

            if (!TryGetStringProperty(element, "type", out var type) || !type.Equals("tool_use", StringComparison.Ordinal)) return false;
            if (!TryGetStringProperty(element, "name", out toolName)) return false;
            if (!element.TryGetProperty("input", out toolInput)) return false;

            return true;
        }

        private static string GetToolUseIdentifier(JsonElement element)
        {
            if (TryGetStringProperty(element, "tool_use_id", out var toolUseIdentifier)) return toolUseIdentifier;
            if (TryGetStringProperty(element, "id", out toolUseIdentifier)) return toolUseIdentifier;
            return string.Empty;
        }

        private static string GetTaskIdentifierFromToolUseElement(JsonElement element)
        {
            if (element.TryGetProperty("tool_response", out var toolResponseElement)) return ExtractTaskIdentifier(toolResponseElement);
            return ExtractTaskIdentifier(element);
        }

        private static string[] ReadRecentTranscriptLines(string transcriptPath)
        {
            try
            {
                using var stream = new FileStream(transcriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length == 0) return [];

                var transcriptLength = stream.Length;
                var bytesToRead = (int)Math.Min(transcriptLength, RecentTranscriptByteLimit);
                var startsAtBeginning = transcriptLength <= bytesToRead;
                var buffer = new byte[bytesToRead];
                stream.Seek(-bytesToRead, SeekOrigin.End);
                var bytesRead = 0;
                while (bytesRead < bytesToRead)
                {
                    var currentBytesRead = stream.Read(buffer, bytesRead, bytesToRead - bytesRead);
                    if (currentBytesRead == 0) break;
                    bytesRead += currentBytesRead;
                }

                var transcriptText = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                if (!startsAtBeginning)
                {
                    var firstNewLineIndex = transcriptText.IndexOf('\n');
                    transcriptText = firstNewLineIndex >= 0 ? transcriptText[(firstNewLineIndex + 1)..] : string.Empty;
                }

                if (string.IsNullOrWhiteSpace(transcriptText)) return [];

                return transcriptText
                    .Split('\n')
                    .Select(transcriptLine => transcriptLine.TrimEnd('\r'))
                    .Where(transcriptLine => !string.IsNullOrWhiteSpace(transcriptLine))
                    .TakeLast(RecentTranscriptLineLimit)
                    .ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException or PathTooLongException)
            {
                return [];
            }
        }
    }

    private readonly record struct ClaudeHookSubagentWorkItem(
        string AgentIdentifier,
        string AgentType,
        string AgentTranscriptPath,
        DateTimeOffset StartedAt);

    private readonly record struct ClaudeHookBackgroundWorkItem(
        string ToolUseIdentifier,
        string TaskIdentifier,
        string ToolName,
        string Summary,
        DateTimeOffset StartedAt);

    private readonly record struct ClaudeHookTaskNotification(
        string TaskIdentifier,
        string ToolUseIdentifier,
        string Status)
    {
        public bool IsTerminal => IsTerminalTaskStatus(Status);
    }

    private sealed record ClaudeHookDeferredStop(
        bool IsProviderSessionEnd,
        string SessionEndReason,
        string PendingProviderWorkReason,
        DateTimeOffset DeferredAt);

    private readonly record struct ClaudeHookPendingWorkSnapshot(
        ClaudeHookSubagentWorkItem[] ActiveSubagents,
        ClaudeHookBackgroundWorkItem[] ActiveBackgroundTasks)
    {
        public bool HasPendingWork => ActiveSubagents.Length > 0 || ActiveBackgroundTasks.Length > 0;

        public string CreatePendingWorkReason(bool stopHookActive)
        {
            var pendingWorkDescriptions = new List<string>();
            if (ActiveSubagents.Length > 0) pendingWorkDescriptions.Add($"{ActiveSubagents.Length} active subagent(s)");
            if (ActiveBackgroundTasks.Length > 0) pendingWorkDescriptions.Add($"{ActiveBackgroundTasks.Length} active background task(s)");

            var reason = "Claude Code still has pending work: "
                + string.Join(", ", pendingWorkDescriptions)
                + ".";
            if (stopHookActive) reason += " The stop hook is already active.";

            return reason;
        }

        public string CreateLogSummary()
        {
            var summaryParts = new List<string>();
            if (ActiveSubagents.Length > 0)
            {
                summaryParts.Add("subagents="
                    + string.Join(
                        ",",
                        ActiveSubagents.Select(subagentWorkItem =>
                            string.IsNullOrWhiteSpace(subagentWorkItem.AgentType)
                                ? subagentWorkItem.AgentIdentifier
                                : $"{subagentWorkItem.AgentType}:{subagentWorkItem.AgentIdentifier}")));
            }

            if (ActiveBackgroundTasks.Length > 0)
            {
                summaryParts.Add("backgroundTasks="
                    + string.Join(
                        ",",
                        ActiveBackgroundTasks.Select(backgroundWorkItem =>
                            string.IsNullOrWhiteSpace(backgroundWorkItem.Summary)
                                ? backgroundWorkItem.ToolUseIdentifier
                                : backgroundWorkItem.Summary)));
            }

            return string.Join(" ", summaryParts);
        }
    }
}
