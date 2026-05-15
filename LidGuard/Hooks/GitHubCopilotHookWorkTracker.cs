using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LidGuard.Hooks;

internal static class GitHubCopilotHookWorkTracker
{
    private const int LockRetryCount = 20;
    private const int LockRetryDelayMilliseconds = 25;
    private const int RecentTranscriptLineLimit = 4096;
    private const int RecentTranscriptByteLimit = 4_194_304;
    private const string ActiveBackgroundTasksPropertyName = "activeBackgroundTasks";
    private const string ActiveSubagentsPropertyName = "activeSubagents";
    private const string AgentWorkKind = "agent";
    private const string CompletedBackgroundTasksPropertyName = "completedBackgroundTasks";
    private const string CompletedSubagentsPropertyName = "completedSubagents";
    private const string ShellWorkKind = "shell";
    private const string StateDirectoryName = "copilot-hook-work-state";

    public static void RecordToolUseEvent(GitHubCopilotHookInput hookInput, string sessionIdentifier)
    {
        ArgumentNullException.ThrowIfNull(hookInput);

        if (TryCreateBackgroundWorkItem(hookInput, out var backgroundWorkItem))
        {
            UpdateSessionState(sessionIdentifier, sessionWorkState => sessionWorkState.UpsertBackgroundTask(backgroundWorkItem));
            return;
        }

        if (TryGetCompletedBackgroundWorkIdentifier(hookInput, out var completedBackgroundWorkIdentifier))
        {
            UpdateSessionState(sessionIdentifier, sessionWorkState => sessionWorkState.RemoveBackgroundTask(completedBackgroundWorkIdentifier));
        }
    }

    public static bool RecordCompletionNotification(GitHubCopilotHookInput hookInput, string sessionIdentifier)
    {
        ArgumentNullException.ThrowIfNull(hookInput);

        var notificationType = hookInput.NotificationType.Trim();
        var notificationText = $"{hookInput.NotificationTitle} {hookInput.NotificationMessage}".Trim();
        if (notificationType.Equals(GitHubCopilotHookEventNames.ShellCompletedNotificationType, StringComparison.Ordinal)
            || notificationType.Equals(GitHubCopilotHookEventNames.ShellDetachedCompletedNotificationType, StringComparison.Ordinal))
        {
            UpdateSessionState(sessionIdentifier, sessionWorkState => sessionWorkState.RemoveBackgroundTaskFromNotification(ShellWorkKind, notificationText));
            return true;
        }

        if (notificationType.Equals(GitHubCopilotHookEventNames.AgentCompletedNotificationType, StringComparison.Ordinal)
            || notificationType.Equals(GitHubCopilotHookEventNames.AgentIdleNotificationType, StringComparison.Ordinal))
        {
            UpdateSessionState(
                sessionIdentifier,
                sessionWorkState =>
                {
                    sessionWorkState.RemoveBackgroundTaskFromNotification(AgentWorkKind, notificationText);
                    sessionWorkState.RemoveSubagentFromNotification(notificationText);
                });
            return true;
        }

        return false;
    }

    public static void RecordSubagentStarted(GitHubCopilotHookInput hookInput, string sessionIdentifier)
    {
        ArgumentNullException.ThrowIfNull(hookInput);

        if (string.IsNullOrWhiteSpace(hookInput.AgentName)) return;

        var subagentWorkItem = new GitHubCopilotHookSubagentWorkItem(
            hookInput.AgentName.Trim(),
            hookInput.AgentDisplayName.Trim(),
            hookInput.TranscriptPath.Trim(),
            DateTimeOffset.UtcNow);
        UpdateSessionState(sessionIdentifier, sessionWorkState => sessionWorkState.UpsertSubagent(subagentWorkItem));
    }

    public static void RecordSubagentStopped(GitHubCopilotHookInput hookInput, string sessionIdentifier)
    {
        ArgumentNullException.ThrowIfNull(hookInput);

        if (string.IsNullOrWhiteSpace(hookInput.AgentName)) return;
        UpdateSessionState(sessionIdentifier, sessionWorkState => sessionWorkState.RemoveSubagent(hookInput.AgentName));
    }

    public static bool TryCreatePendingWorkReason(GitHubCopilotHookInput hookInput, string sessionIdentifier, out string reason)
    {
        ArgumentNullException.ThrowIfNull(hookInput);

        reason = string.Empty;
        SynchronizeWorkFromTranscript(hookInput.TranscriptPath, sessionIdentifier);

        var sessionWorkState = ReadSessionState(sessionIdentifier);
        var pendingWorkSnapshot = new GitHubCopilotHookPendingWorkSnapshot(
            [.. sessionWorkState.ActiveSubagents],
            [.. sessionWorkState.ActiveBackgroundTasks]);
        if (!pendingWorkSnapshot.HasPendingWork) return false;

        reason = pendingWorkSnapshot.CreatePendingWorkReason();
        GitHubCopilotHookEventLog.AppendMessage($"LidGuard GitHub Copilot hook deferred stop because pending work remains: {pendingWorkSnapshot.CreateLogSummary()}");
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
            sessionWorkState => sessionWorkState.DeferredStop = new GitHubCopilotHookDeferredStop(
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
            GitHubCopilotHookEventLog.AppendMessage($"LidGuard GitHub Copilot hook could not clear work state: {exception.Message}");
        }
    }

    private static void SynchronizeWorkFromTranscript(string transcriptPath, string sessionIdentifier)
    {
        var resolvedTranscriptPath = ResolveTranscriptPath(transcriptPath, sessionIdentifier);
        if (string.IsNullOrWhiteSpace(resolvedTranscriptPath)) return;

        var transcriptWorkSnapshot = GitHubCopilotHookTranscriptWorkScanner.Scan(resolvedTranscriptPath);
        if (transcriptWorkSnapshot.IsEmpty) return;

        UpdateSessionState(sessionIdentifier, sessionWorkState => sessionWorkState.ApplyTranscriptWorkSnapshot(transcriptWorkSnapshot));
    }

    private static bool TryCreateBackgroundWorkItem(GitHubCopilotHookInput hookInput, out GitHubCopilotHookBackgroundWorkItem backgroundWorkItem)
    {
        backgroundWorkItem = default;
        var hasDifferentHookEventName = !IsPostToolUseEventName(hookInput.HookEventName)
            && !string.IsNullOrWhiteSpace(hookInput.HookEventName);
        if (hasDifferentHookEventName) return false;

        return TryCreateBackgroundWorkItem(
            hookInput.ToolName,
            hookInput.ToolInput,
            hookInput.ToolResult,
            string.Empty,
            out backgroundWorkItem);
    }

    private static bool TryCreateBackgroundWorkItem(
        string toolName,
        JsonElement toolInput,
        JsonElement toolResult,
        string toolCallIdentifier,
        out GitHubCopilotHookBackgroundWorkItem backgroundWorkItem)
    {
        backgroundWorkItem = default;
        var normalizedToolName = toolName.Trim();
        if (normalizedToolName.Equals(GitHubCopilotHookEventNames.TaskToolName, StringComparison.Ordinal)
            && IsBackgroundTaskStart(toolInput, toolResult))
        {
            var agentIdentifier = ExtractAgentIdentifier(toolInput, toolResult);
            if (string.IsNullOrWhiteSpace(agentIdentifier)) agentIdentifier = toolCallIdentifier.Trim();
            if (string.IsNullOrWhiteSpace(agentIdentifier)) return false;

            backgroundWorkItem = new GitHubCopilotHookBackgroundWorkItem(
                agentIdentifier,
                normalizedToolName,
                AgentWorkKind,
                CreateBackgroundWorkSummary(normalizedToolName, toolInput, agentIdentifier),
                DateTimeOffset.UtcNow);
            return true;
        }

        if (IsShellToolName(normalizedToolName) && IsBackgroundShellStart(toolInput, toolResult))
        {
            var shellIdentifier = ExtractShellIdentifier(toolInput, toolResult);
            if (string.IsNullOrWhiteSpace(shellIdentifier)) shellIdentifier = toolCallIdentifier.Trim();
            if (string.IsNullOrWhiteSpace(shellIdentifier)) return false;

            backgroundWorkItem = new GitHubCopilotHookBackgroundWorkItem(
                shellIdentifier,
                normalizedToolName,
                ShellWorkKind,
                CreateBackgroundWorkSummary(normalizedToolName, toolInput, shellIdentifier),
                DateTimeOffset.UtcNow);
            return true;
        }

        if (normalizedToolName.Equals(GitHubCopilotHookEventNames.WriteAgentToolName, StringComparison.Ordinal))
        {
            var agentIdentifier = ExtractAgentIdentifier(toolInput, toolResult);
            if (string.IsNullOrWhiteSpace(agentIdentifier)) return false;

            backgroundWorkItem = new GitHubCopilotHookBackgroundWorkItem(
                agentIdentifier,
                normalizedToolName,
                AgentWorkKind,
                CreateBackgroundWorkSummary(normalizedToolName, toolInput, agentIdentifier),
                DateTimeOffset.UtcNow);
            return true;
        }

        return false;
    }

    private static bool TryGetCompletedBackgroundWorkIdentifier(GitHubCopilotHookInput hookInput, out string backgroundWorkIdentifier)
    {
        backgroundWorkIdentifier = string.Empty;
        var normalizedToolName = hookInput.ToolName.Trim();
        if (normalizedToolName.Equals(GitHubCopilotHookEventNames.ReadAgentToolName, StringComparison.Ordinal)
            && IsTerminalReadAgentResult(hookInput.ToolResult))
        {
            backgroundWorkIdentifier = ExtractAgentIdentifier(hookInput.ToolInput, hookInput.ToolResult);
            return !string.IsNullOrWhiteSpace(backgroundWorkIdentifier);
        }

        return false;
    }

    private static void UpdateSessionState(string sessionIdentifier, Action<GitHubCopilotHookSessionWorkState> updateState)
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
            GitHubCopilotHookEventLog.AppendMessage($"LidGuard GitHub Copilot hook could not update work state: {exception.Message}");
        }
    }

    private static GitHubCopilotHookSessionWorkState ReadSessionState(string sessionIdentifier)
    {
        if (string.IsNullOrWhiteSpace(sessionIdentifier)) return new GitHubCopilotHookSessionWorkState();

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
            GitHubCopilotHookEventLog.AppendMessage($"LidGuard GitHub Copilot hook could not read work state: {exception.Message}");
            return new GitHubCopilotHookSessionWorkState();
        }
    }

    private static GitHubCopilotHookSessionWorkState ReadSessionStateWithoutLock(string stateFilePath)
    {
        if (!File.Exists(stateFilePath)) return new GitHubCopilotHookSessionWorkState();

        try
        {
            var rootNode = JsonNode.Parse(File.ReadAllText(stateFilePath));
            if (rootNode is not JsonObject rootObject) return new GitHubCopilotHookSessionWorkState();

            var sessionWorkState = new GitHubCopilotHookSessionWorkState();
            if (rootObject["deferredStop"] is JsonObject deferredStopObject)
            {
                sessionWorkState.DeferredStop = new GitHubCopilotHookDeferredStop(
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

                    sessionWorkState.UpsertSubagent(new GitHubCopilotHookSubagentWorkItem(
                        agentIdentifier,
                        GetStringProperty(activeSubagentObject, "agentDisplayName"),
                        GetStringProperty(activeSubagentObject, "agentTranscriptPath"),
                        GetDateTimeOffsetProperty(activeSubagentObject, "startedAt")));
                }
            }

            if (rootObject[ActiveBackgroundTasksPropertyName] is JsonArray activeBackgroundTasks)
            {
                foreach (var activeBackgroundTaskNode in activeBackgroundTasks)
                {
                    if (activeBackgroundTaskNode is not JsonObject activeBackgroundTaskObject) continue;

                    var workIdentifier = GetStringProperty(activeBackgroundTaskObject, "workIdentifier");
                    if (string.IsNullOrWhiteSpace(workIdentifier)) continue;

                    sessionWorkState.UpsertBackgroundTask(new GitHubCopilotHookBackgroundWorkItem(
                        workIdentifier,
                        GetStringProperty(activeBackgroundTaskObject, "toolName"),
                        GetStringProperty(activeBackgroundTaskObject, "workKind"),
                        GetStringProperty(activeBackgroundTaskObject, "summary"),
                        GetDateTimeOffsetProperty(activeBackgroundTaskObject, "startedAt")));
                }
            }

            ReadStringSet(rootObject, CompletedSubagentsPropertyName, sessionWorkState.CompletedSubagentIdentifiers);
            ReadStringSet(rootObject, CompletedBackgroundTasksPropertyName, sessionWorkState.CompletedBackgroundWorkIdentifiers);
            return sessionWorkState;
        }
        catch (JsonException) { return new GitHubCopilotHookSessionWorkState(); }
    }

    private static void WriteSessionStateWithoutLock(
        string stateFilePath,
        string sessionIdentifier,
        GitHubCopilotHookSessionWorkState sessionWorkState)
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
            [ActiveBackgroundTasksPropertyName] = CreateBackgroundTasksArray(sessionWorkState.ActiveBackgroundTasks),
            [CompletedSubagentsPropertyName] = CreateStringArray(sessionWorkState.CompletedSubagentIdentifiers),
            [CompletedBackgroundTasksPropertyName] = CreateStringArray(sessionWorkState.CompletedBackgroundWorkIdentifiers)
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

    private static void ReadStringSet(JsonObject rootObject, string propertyName, HashSet<string> values)
    {
        if (rootObject[propertyName] is not JsonArray valueArray) return;
        foreach (var valueNode in valueArray)
        {
            if (valueNode is not JsonValue jsonValue) continue;
            if (!jsonValue.TryGetValue<string>(out var value)) continue;
            if (!string.IsNullOrWhiteSpace(value)) values.Add(value);
        }
    }

    private static JsonArray CreateStringArray(IEnumerable<string> values)
    {
        var jsonArray = new JsonArray();
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            jsonArray.Add((JsonNode)JsonValue.Create(value.Trim())!);
        }

        return jsonArray;
    }

    private static JsonArray CreateSubagentsArray(IEnumerable<GitHubCopilotHookSubagentWorkItem> subagentWorkItems)
    {
        var subagentsArray = new JsonArray();
        foreach (var subagentWorkItem in subagentWorkItems)
        {
            subagentsArray.Add((JsonNode)new JsonObject
            {
                ["agentIdentifier"] = subagentWorkItem.AgentIdentifier,
                ["agentDisplayName"] = subagentWorkItem.AgentDisplayName,
                ["agentTranscriptPath"] = subagentWorkItem.AgentTranscriptPath,
                ["startedAt"] = subagentWorkItem.StartedAt.ToString("O", CultureInfo.InvariantCulture)
            });
        }

        return subagentsArray;
    }

    private static JsonArray CreateBackgroundTasksArray(IEnumerable<GitHubCopilotHookBackgroundWorkItem> backgroundWorkItems)
    {
        var backgroundTasksArray = new JsonArray();
        foreach (var backgroundWorkItem in backgroundWorkItems)
        {
            backgroundTasksArray.Add((JsonNode)new JsonObject
            {
                ["workIdentifier"] = backgroundWorkItem.WorkIdentifier,
                ["toolName"] = backgroundWorkItem.ToolName,
                ["workKind"] = backgroundWorkItem.WorkKind,
                ["summary"] = backgroundWorkItem.Summary,
                ["startedAt"] = backgroundWorkItem.StartedAt.ToString("O", CultureInfo.InvariantCulture)
            });
        }

        return backgroundTasksArray;
    }

    private static bool IsBackgroundTaskStart(JsonElement toolInput, JsonElement toolResult)
    {
        if (TryGetStringProperty(toolInput, "mode", out var mode) && mode.Equals("background", StringComparison.OrdinalIgnoreCase)) return true;
        if (TryFindStringProperty(toolResult, "execution_mode", out var executionMode) && executionMode.Equals("background", StringComparison.OrdinalIgnoreCase)) return true;
        if (TryFindStringProperty(toolResult, "executionMode", out executionMode) && executionMode.Equals("background", StringComparison.OrdinalIgnoreCase)) return true;

        var toolResultText = GetToolResultText(toolResult);
        return toolResultText.Contains("Agent started in background", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBackgroundShellStart(JsonElement toolInput, JsonElement toolResult)
    {
        if (TryGetStringProperty(toolInput, "mode", out var mode) && mode.Equals("async", StringComparison.OrdinalIgnoreCase)) return true;
        if (TryFindStringProperty(toolResult, "executionMode", out var executionMode) && executionMode.Equals("async", StringComparison.OrdinalIgnoreCase)) return true;
        if (TryFindStringProperty(toolResult, "execution_mode", out executionMode) && executionMode.Equals("async", StringComparison.OrdinalIgnoreCase)) return true;
        if (TryFindStringProperty(toolResult, "detached", out var detached) && detached.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;

        var toolResultText = GetToolResultText(toolResult);
        return toolResultText.Contains("command started in background", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTerminalReadAgentResult(JsonElement toolResult)
    {
        var toolResultText = GetToolResultText(toolResult);
        if (string.IsNullOrWhiteSpace(toolResultText)) return false;
        if (toolResultText.Contains("still running", StringComparison.OrdinalIgnoreCase)) return false;
        if (toolResultText.Contains("status: running", StringComparison.OrdinalIgnoreCase)) return false;

        return toolResultText.Contains("Agent is idle", StringComparison.OrdinalIgnoreCase)
            || toolResultText.Contains("status: idle", StringComparison.OrdinalIgnoreCase)
            || toolResultText.Contains("status: completed", StringComparison.OrdinalIgnoreCase)
            || toolResultText.Contains("status: failed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsShellToolName(string toolName)
        => toolName.Equals(GitHubCopilotHookEventNames.BashToolName, StringComparison.Ordinal)
            || toolName.Equals(GitHubCopilotHookEventNames.PowerShellToolName, StringComparison.Ordinal);

    private static bool IsPostToolUseEventName(string hookEventName)
        => hookEventName.Trim().Equals(GitHubCopilotHookEventNames.PostToolUse, StringComparison.Ordinal)
            || hookEventName.Trim().Equals(GitHubCopilotHookEventNames.PascalCasePostToolUseAlias, StringComparison.Ordinal);

    private static string ExtractAgentIdentifier(JsonElement toolInput, JsonElement toolResult)
    {
        if (TryFindStringProperty(toolResult, "agent_id", out var agentIdentifier)) return agentIdentifier;
        if (TryFindStringProperty(toolResult, "agentId", out agentIdentifier)) return agentIdentifier;
        if (TryFindStringProperty(toolInput, "agent_id", out agentIdentifier)) return agentIdentifier;
        if (TryFindStringProperty(toolInput, "agentId", out agentIdentifier)) return agentIdentifier;
        if (TryFindStringProperty(toolInput, "name", out agentIdentifier)) return agentIdentifier;

        var toolResultText = GetToolResultText(toolResult);
        return ExtractMarkerText(toolResultText, "agent_id:");
    }

    private static string ExtractShellIdentifier(JsonElement toolInput, JsonElement toolResult)
    {
        if (TryFindStringProperty(toolInput, "shellId", out var shellIdentifier)) return shellIdentifier;
        if (TryFindStringProperty(toolInput, "shell_id", out shellIdentifier)) return shellIdentifier;
        if (TryFindStringProperty(toolResult, "shellId", out shellIdentifier)) return shellIdentifier;
        if (TryFindStringProperty(toolResult, "shell_id", out shellIdentifier)) return shellIdentifier;

        var toolResultText = GetToolResultText(toolResult);
        return ExtractMarkerText(toolResultText, "shellId:");
    }

    private static string ExtractMarkerText(string content, string marker)
    {
        if (string.IsNullOrWhiteSpace(content)) return string.Empty;

        var markerIndex = content.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0) return string.Empty;

        var valueStartIndex = markerIndex + marker.Length;
        while (valueStartIndex < content.Length && char.IsWhiteSpace(content[valueStartIndex])) valueStartIndex++;
        if (valueStartIndex >= content.Length) return string.Empty;

        var valueEndIndex = valueStartIndex;
        while (valueEndIndex < content.Length && !char.IsWhiteSpace(content[valueEndIndex]) && content[valueEndIndex] is not '<' and not '>' and not ')' and not '(') valueEndIndex++;

        return content[valueStartIndex..valueEndIndex].Trim().TrimEnd('.', ',', ';', ':');
    }

    private static string CreateBackgroundWorkSummary(string toolName, JsonElement toolInput, string workIdentifier)
    {
        var detail = string.Empty;
        var hasBackgroundWorkDetail = TryGetStringProperty(toolInput, "description", out detail)
            || TryGetStringProperty(toolInput, "command", out detail)
            || TryGetStringProperty(toolInput, "prompt", out detail)
            || TryGetStringProperty(toolInput, "agent_type", out detail)
            || TryGetStringProperty(toolInput, "name", out detail);
        if (!hasBackgroundWorkDetail) detail = string.Empty;

        if (detail.Length > 80) detail = detail[..77] + "...";

        if (string.IsNullOrWhiteSpace(detail)) return $"{toolName} {workIdentifier}".Trim();
        return $"{toolName} {workIdentifier}: {detail}".Trim();
    }

    private static string GetToolResultText(JsonElement toolResult)
    {
        if (TryFindStringProperty(toolResult, "textResultForLlm", out var textResult)) return textResult;
        if (TryFindStringProperty(toolResult, "text_result_for_llm", out textResult)) return textResult;
        if (TryFindStringProperty(toolResult, "sessionLog", out textResult)) return textResult;
        if (TryFindStringProperty(toolResult, "content", out textResult)) return textResult;
        if (TryFindStringProperty(toolResult, "detailedContent", out textResult)) return textResult;
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
                    return !string.IsNullOrWhiteSpace(value);
                }

                if (TryFindStringProperty(property.Value, propertyName, out value)) return true;
            }
        }

        if (element.ValueKind != JsonValueKind.Array) return false;
        foreach (var itemElement in element.EnumerateArray())
        {
            if (TryFindStringProperty(itemElement, propertyName, out value)) return true;
        }

        return false;
    }

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

    private static string ResolveTranscriptPath(string transcriptPath, string sessionIdentifier)
    {
        if (!string.IsNullOrWhiteSpace(transcriptPath)) return transcriptPath;
        if (string.IsNullOrWhiteSpace(sessionIdentifier) || sessionIdentifier.Contains(':', StringComparison.Ordinal)) return string.Empty;

        var gitHubCopilotHomeDirectoryPath = Environment.GetEnvironmentVariable("COPILOT_HOME");
        if (string.IsNullOrWhiteSpace(gitHubCopilotHomeDirectoryPath))
        {
            var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(userProfilePath)) return string.Empty;
            gitHubCopilotHomeDirectoryPath = Path.Combine(userProfilePath, ".copilot");
        }

        return Path.Combine(gitHubCopilotHomeDirectoryPath, "session-state", sessionIdentifier, "events.jsonl");
    }

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

    private sealed class GitHubCopilotHookSessionWorkState
    {
        public List<GitHubCopilotHookSubagentWorkItem> ActiveSubagents { get; } = [];

        public List<GitHubCopilotHookBackgroundWorkItem> ActiveBackgroundTasks { get; } = [];

        public HashSet<string> CompletedSubagentIdentifiers { get; } = new(StringComparer.Ordinal);

        public HashSet<string> CompletedBackgroundWorkIdentifiers { get; } = new(StringComparer.Ordinal);

        public GitHubCopilotHookDeferredStop DeferredStop { get; set; }

        public bool HasPendingWork => ActiveSubagents.Count > 0 || ActiveBackgroundTasks.Count > 0;

        public bool HasDeferredStop => DeferredStop is not null;

        public bool ShouldPersist => HasPendingWork || HasDeferredStop;

        public void UpsertSubagent(GitHubCopilotHookSubagentWorkItem subagentWorkItem)
        {
            CompletedSubagentIdentifiers.Remove(subagentWorkItem.AgentIdentifier);
            ActiveSubagents.RemoveAll(existingSubagentWorkItem => existingSubagentWorkItem.AgentIdentifier.Equals(subagentWorkItem.AgentIdentifier, StringComparison.Ordinal));
            ActiveSubagents.Add(subagentWorkItem);
        }

        public void RemoveSubagent(string agentIdentifier)
        {
            if (string.IsNullOrWhiteSpace(agentIdentifier)) return;

            var normalizedAgentIdentifier = agentIdentifier.Trim();
            CompletedSubagentIdentifiers.Add(normalizedAgentIdentifier);
            ActiveSubagents.RemoveAll(subagentWorkItem => subagentWorkItem.AgentIdentifier.Equals(normalizedAgentIdentifier, StringComparison.Ordinal));
        }

        public void RemoveSubagentFromNotification(string notificationText)
        {
            var removedCount = ActiveSubagents.RemoveAll(subagentWorkItem =>
                !string.IsNullOrWhiteSpace(subagentWorkItem.AgentIdentifier)
                && notificationText.Contains(subagentWorkItem.AgentIdentifier, StringComparison.Ordinal));
            if (removedCount > 0) return;

            if (ActiveSubagents.Count == 1) RemoveSubagent(ActiveSubagents[0].AgentIdentifier);
        }

        public void UpsertBackgroundTask(GitHubCopilotHookBackgroundWorkItem backgroundWorkItem)
        {
            CompletedBackgroundWorkIdentifiers.Remove(backgroundWorkItem.WorkIdentifier);
            ActiveBackgroundTasks.RemoveAll(existingBackgroundWorkItem => existingBackgroundWorkItem.WorkIdentifier.Equals(backgroundWorkItem.WorkIdentifier, StringComparison.Ordinal));
            ActiveBackgroundTasks.Add(backgroundWorkItem);
        }

        public void RemoveBackgroundTask(string workIdentifier)
        {
            if (string.IsNullOrWhiteSpace(workIdentifier)) return;

            var normalizedWorkIdentifier = workIdentifier.Trim();
            CompletedBackgroundWorkIdentifiers.Add(normalizedWorkIdentifier);
            ActiveBackgroundTasks.RemoveAll(backgroundWorkItem => backgroundWorkItem.WorkIdentifier.Equals(normalizedWorkIdentifier, StringComparison.Ordinal));
        }

        public void RemoveBackgroundTaskFromNotification(string workKind, string notificationText)
        {
            var removedCount = ActiveBackgroundTasks.RemoveAll(backgroundWorkItem =>
                backgroundWorkItem.WorkKind.Equals(workKind, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(backgroundWorkItem.WorkIdentifier)
                && notificationText.Contains(backgroundWorkItem.WorkIdentifier, StringComparison.Ordinal));
            if (removedCount > 0) return;

            var matchingBackgroundTasks = ActiveBackgroundTasks
                .Where(backgroundWorkItem => backgroundWorkItem.WorkKind.Equals(workKind, StringComparison.Ordinal))
                .ToArray();
            if (matchingBackgroundTasks.Length == 1) RemoveBackgroundTask(matchingBackgroundTasks[0].WorkIdentifier);
        }

        public void ApplyTranscriptWorkSnapshot(GitHubCopilotHookTranscriptWorkSnapshot transcriptWorkSnapshot)
        {
            foreach (var completedSubagentIdentifier in transcriptWorkSnapshot.CompletedSubagentIdentifiers) RemoveSubagent(completedSubagentIdentifier);
            foreach (var completedBackgroundWorkIdentifier in transcriptWorkSnapshot.CompletedBackgroundWorkIdentifiers) RemoveBackgroundTask(completedBackgroundWorkIdentifier);
            foreach (var subagentWorkItem in transcriptWorkSnapshot.StartedSubagents)
            {
                if (transcriptWorkSnapshot.IsCompleted(subagentWorkItem)) continue;
                if (CompletedSubagentIdentifiers.Contains(subagentWorkItem.AgentIdentifier)) continue;
                UpsertSubagent(subagentWorkItem);
            }

            foreach (var backgroundWorkItem in transcriptWorkSnapshot.StartedBackgroundTasks)
            {
                if (transcriptWorkSnapshot.IsCompleted(backgroundWorkItem)) continue;
                if (CompletedBackgroundWorkIdentifiers.Contains(backgroundWorkItem.WorkIdentifier)) continue;
                UpsertBackgroundTask(backgroundWorkItem);
            }
        }
    }

    private sealed class GitHubCopilotHookTranscriptWorkSnapshot
    {
        public List<GitHubCopilotHookSubagentWorkItem> StartedSubagents { get; } = [];

        public List<GitHubCopilotHookBackgroundWorkItem> StartedBackgroundTasks { get; } = [];

        public HashSet<string> CompletedSubagentIdentifiers { get; } = new(StringComparer.Ordinal);

        public HashSet<string> CompletedBackgroundWorkIdentifiers { get; } = new(StringComparer.Ordinal);

        public bool IsEmpty => StartedSubagents.Count == 0
            && StartedBackgroundTasks.Count == 0
            && CompletedSubagentIdentifiers.Count == 0
            && CompletedBackgroundWorkIdentifiers.Count == 0;

        public void AddStartedSubagent(GitHubCopilotHookSubagentWorkItem subagentWorkItem)
        {
            if (StartedSubagents.Any(existingSubagentWorkItem => existingSubagentWorkItem.AgentIdentifier.Equals(subagentWorkItem.AgentIdentifier, StringComparison.Ordinal))) return;
            StartedSubagents.Add(subagentWorkItem);
        }

        public void AddStartedBackgroundTask(GitHubCopilotHookBackgroundWorkItem backgroundWorkItem)
        {
            if (StartedBackgroundTasks.Any(existingBackgroundWorkItem => existingBackgroundWorkItem.WorkIdentifier.Equals(backgroundWorkItem.WorkIdentifier, StringComparison.Ordinal))) return;
            StartedBackgroundTasks.Add(backgroundWorkItem);
        }

        public bool IsCompleted(GitHubCopilotHookSubagentWorkItem subagentWorkItem)
            => CompletedSubagentIdentifiers.Contains(subagentWorkItem.AgentIdentifier);

        public bool IsCompleted(GitHubCopilotHookBackgroundWorkItem backgroundWorkItem)
            => CompletedBackgroundWorkIdentifiers.Contains(backgroundWorkItem.WorkIdentifier);
    }

    private static class GitHubCopilotHookTranscriptWorkScanner
    {
        public static GitHubCopilotHookTranscriptWorkSnapshot Scan(string transcriptPath)
        {
            var transcriptWorkSnapshot = new GitHubCopilotHookTranscriptWorkSnapshot();
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

        private static void InspectElement(JsonElement element, GitHubCopilotHookTranscriptWorkSnapshot transcriptWorkSnapshot)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                InspectHookStartEvent(element, transcriptWorkSnapshot);
                InspectToolExecutionEvent(element, transcriptWorkSnapshot);
                foreach (var property in element.EnumerateObject()) InspectElement(property.Value, transcriptWorkSnapshot);
                return;
            }

            if (element.ValueKind != JsonValueKind.Array) return;
            foreach (var itemElement in element.EnumerateArray()) InspectElement(itemElement, transcriptWorkSnapshot);
        }

        private static void InspectHookStartEvent(JsonElement element, GitHubCopilotHookTranscriptWorkSnapshot transcriptWorkSnapshot)
        {
            if (!TryGetStringProperty(element, "type", out var type) || !type.Equals("hook.start", StringComparison.Ordinal)) return;
            if (!element.TryGetProperty("data", out var dataElement)) return;
            if (!TryGetStringProperty(dataElement, "hookType", out var hookType)) return;
            if (!dataElement.TryGetProperty("input", out var inputElement)) return;

            if (hookType.Equals(GitHubCopilotHookEventNames.SubagentStart, StringComparison.Ordinal)
                && TryCreateSubagentWorkItem(inputElement, out var subagentWorkItem))
            {
                transcriptWorkSnapshot.AddStartedSubagent(subagentWorkItem);
                return;
            }

            if (hookType.Equals(GitHubCopilotHookEventNames.SubagentStop, StringComparison.Ordinal)
                && TryGetStringProperty(inputElement, "agentName", out var stoppedAgentIdentifier))
            {
                transcriptWorkSnapshot.CompletedSubagentIdentifiers.Add(stoppedAgentIdentifier);
                return;
            }

            if (!hookType.Equals(GitHubCopilotHookEventNames.PostToolUse, StringComparison.Ordinal)) return;
            if (!TryGetStringProperty(inputElement, "toolName", out var toolName)) return;
            var toolInput = inputElement.TryGetProperty("toolArgs", out var toolInputElement) ? toolInputElement : default;
            var toolResult = inputElement.TryGetProperty("toolResult", out var toolResultElement) ? toolResultElement : default;

            if (TryCreateBackgroundWorkItem(toolName, toolInput, toolResult, string.Empty, out var backgroundWorkItem))
            {
                transcriptWorkSnapshot.AddStartedBackgroundTask(backgroundWorkItem);
                return;
            }

            if (toolName.Equals(GitHubCopilotHookEventNames.ReadAgentToolName, StringComparison.Ordinal)
                && IsTerminalReadAgentResult(toolResult))
            {
                var agentIdentifier = ExtractAgentIdentifier(toolInput, toolResult);
                if (!string.IsNullOrWhiteSpace(agentIdentifier)) transcriptWorkSnapshot.CompletedBackgroundWorkIdentifiers.Add(agentIdentifier);
            }
        }

        private static void InspectToolExecutionEvent(JsonElement element, GitHubCopilotHookTranscriptWorkSnapshot transcriptWorkSnapshot)
        {
            if (!TryGetStringProperty(element, "type", out var type)) return;
            var isToolExecutionEvent = type.Equals("tool.execution_start", StringComparison.Ordinal)
                || type.Equals("tool.execution_complete", StringComparison.Ordinal);
            if (!isToolExecutionEvent) return;
            if (!element.TryGetProperty("data", out var dataElement)) return;
            if (!TryGetStringProperty(dataElement, "toolName", out var toolName)) return;

            var toolCallIdentifier = TryGetStringProperty(dataElement, "toolCallId", out var currentToolCallIdentifier)
                ? currentToolCallIdentifier
                : string.Empty;
            var toolInput = dataElement.TryGetProperty("arguments", out var toolInputElement) ? toolInputElement : default;
            var toolResult = dataElement.TryGetProperty("result", out var toolResultElement) ? toolResultElement : default;

            if (TryCreateBackgroundWorkItem(toolName, toolInput, toolResult, toolCallIdentifier, out var backgroundWorkItem))
            {
                transcriptWorkSnapshot.AddStartedBackgroundTask(backgroundWorkItem);
                return;
            }

            if (type.Equals("tool.execution_complete", StringComparison.Ordinal)
                && toolName.Equals(GitHubCopilotHookEventNames.ReadAgentToolName, StringComparison.Ordinal)
                && IsTerminalReadAgentResult(toolResult))
            {
                var agentIdentifier = ExtractAgentIdentifier(toolInput, toolResult);
                if (!string.IsNullOrWhiteSpace(agentIdentifier)) transcriptWorkSnapshot.CompletedBackgroundWorkIdentifiers.Add(agentIdentifier);
            }
        }

        private static bool TryCreateSubagentWorkItem(JsonElement inputElement, out GitHubCopilotHookSubagentWorkItem subagentWorkItem)
        {
            subagentWorkItem = default;
            if (!TryGetStringProperty(inputElement, "agentName", out var agentIdentifier)) return false;

            var agentDisplayName = TryGetStringProperty(inputElement, "agentDisplayName", out var currentAgentDisplayName)
                ? currentAgentDisplayName
                : string.Empty;
            var transcriptPath = TryGetStringProperty(inputElement, "transcriptPath", out var currentTranscriptPath)
                ? currentTranscriptPath
                : string.Empty;
            subagentWorkItem = new GitHubCopilotHookSubagentWorkItem(
                agentIdentifier,
                agentDisplayName,
                transcriptPath,
                DateTimeOffset.UtcNow);
            return true;
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

    private readonly record struct GitHubCopilotHookSubagentWorkItem(
        string AgentIdentifier,
        string AgentDisplayName,
        string AgentTranscriptPath,
        DateTimeOffset StartedAt);

    private readonly record struct GitHubCopilotHookBackgroundWorkItem(
        string WorkIdentifier,
        string ToolName,
        string WorkKind,
        string Summary,
        DateTimeOffset StartedAt);

    private sealed record GitHubCopilotHookDeferredStop(
        bool IsProviderSessionEnd,
        string SessionEndReason,
        string PendingProviderWorkReason,
        DateTimeOffset DeferredAt);

    private readonly record struct GitHubCopilotHookPendingWorkSnapshot(
        GitHubCopilotHookSubagentWorkItem[] ActiveSubagents,
        GitHubCopilotHookBackgroundWorkItem[] ActiveBackgroundTasks)
    {
        public bool HasPendingWork => ActiveSubagents.Length > 0 || ActiveBackgroundTasks.Length > 0;

        public string CreatePendingWorkReason()
        {
            var pendingWorkDescriptions = new List<string>();
            if (ActiveSubagents.Length > 0) pendingWorkDescriptions.Add($"{ActiveSubagents.Length} active subagent(s)");
            if (ActiveBackgroundTasks.Length > 0) pendingWorkDescriptions.Add($"{ActiveBackgroundTasks.Length} active background task(s)");

            return "GitHub Copilot still has pending work: "
                + string.Join(", ", pendingWorkDescriptions)
                + ".";
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
                            string.IsNullOrWhiteSpace(subagentWorkItem.AgentDisplayName)
                                ? subagentWorkItem.AgentIdentifier
                                : $"{subagentWorkItem.AgentDisplayName}:{subagentWorkItem.AgentIdentifier}")));
            }

            if (ActiveBackgroundTasks.Length > 0)
            {
                summaryParts.Add("backgroundTasks="
                    + string.Join(
                        ",",
                        ActiveBackgroundTasks.Select(backgroundWorkItem =>
                            string.IsNullOrWhiteSpace(backgroundWorkItem.Summary)
                                ? backgroundWorkItem.WorkIdentifier
                                : backgroundWorkItem.Summary)));
            }

            return string.Join(" ", summaryParts);
        }
    }
}
