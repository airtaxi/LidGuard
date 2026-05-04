using System.Text.Json;
using LidGuard.Ipc;
using LidGuard.Settings;
using LidGuard.Hooks;
using LidGuard.Sessions;

namespace LidGuard.Hooks;

internal static class ClaudeHookCommand
{
    private const string HooksJsonFormat = "hooks-json";
    private const string SettingsJsonFormat = "settings-json";

    public static async Task<int> RunAsync()
    {
        var hookInputJson = await Console.In.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(hookInputJson))
        {
            ClaudeHookEventLog.AppendMessage("LidGuard Claude hook received empty input.");
            return 0;
        }

        ClaudeHookInput hookInput;
        try
        {
            hookInput = JsonSerializer.Deserialize(hookInputJson, LidGuardJsonSerializerContext.Default.ClaudeHookInput);
        }
        catch (JsonException exception)
        {
            ClaudeHookEventLog.AppendMessage($"LidGuard Claude hook could not parse input: {exception.Message}");
            return 0;
        }

        if (hookInput is null)
        {
            ClaudeHookEventLog.AppendMessage("LidGuard Claude hook could not parse input.");
            return 0;
        }

        ClaudeHookEventLog.AppendReceived(hookInput);
        var hookEventName = hookInput.HookEventName.Trim();
        if (hookEventName.Equals(ClaudeHookEventNames.Notification, StringComparison.Ordinal)) return await HandleNotificationAsync(hookInput);
        if (hookEventName.Equals(ClaudeHookEventNames.SubagentStart, StringComparison.Ordinal))
        {
            ClaudeHookWorkTracker.RecordSubagentStarted(hookInput, GetSessionIdentifier(hookInput));
            return await ReportActivityAsync(hookInput, hookInput.AgentType);
        }

        if (hookEventName.Equals(ClaudeHookEventNames.SubagentStop, StringComparison.Ordinal))
        {
            ClaudeHookWorkTracker.RecordSubagentStopped(hookInput, GetSessionIdentifier(hookInput));
            var deferredStopExitCode = await TrySendDeferredStopAsync(hookInput);
            if (deferredStopExitCode.HasValue) return deferredStopExitCode.Value;

            return await ReportActivityAsync(hookInput, hookInput.AgentType);
        }

        if (hookEventName.Equals(ClaudeHookEventNames.TaskCreated, StringComparison.Ordinal))
        {
            ClaudeHookWorkTracker.RecordTaskCreated(hookInput, GetSessionIdentifier(hookInput));
            return await ReportActivityAsync(hookInput, hookInput.TaskIdentifier);
        }

        if (hookEventName.Equals(ClaudeHookEventNames.TaskCompleted, StringComparison.Ordinal))
        {
            ClaudeHookWorkTracker.RecordTaskCompleted(hookInput, GetSessionIdentifier(hookInput));
            var deferredStopExitCode = await TrySendDeferredStopAsync(hookInput);
            if (deferredStopExitCode.HasValue) return deferredStopExitCode.Value;

            return await ReportActivityAsync(hookInput, hookInput.TaskIdentifier);
        }

        if (hookEventName.Equals(ClaudeHookEventNames.PreToolUse, StringComparison.Ordinal)
            || hookEventName.Equals(ClaudeHookEventNames.PostToolUse, StringComparison.Ordinal))
        {
            ClaudeHookWorkTracker.RecordToolUseEvent(hookInput, GetSessionIdentifier(hookInput));
            var deferredStopExitCode = await TrySendDeferredStopAsync(hookInput);
            if (deferredStopExitCode.HasValue) return deferredStopExitCode.Value;

            return await ReportActivityAsync(hookInput);
        }

        if (hookEventName.Equals(ClaudeHookEventNames.PostToolUseFailure, StringComparison.Ordinal))
        {
            return hookInput.IsInterrupt
                ? await SendRuntimeRequestAsync(LidGuardPipeCommands.Stop, hookInput)
                : await ReportActivityAsync(hookInput);
        }

        if (hookEventName.Equals(ClaudeHookEventNames.UserPromptSubmit, StringComparison.Ordinal))
        {
            if (ClaudeHookWorkTracker.TryRecordTaskNotification(hookInput, GetSessionIdentifier(hookInput)))
            {
                var deferredStopExitCode = await TrySendDeferredStopAsync(hookInput);
                if (deferredStopExitCode.HasValue) return deferredStopExitCode.Value;

                return await ReportActivityAsync(hookInput, "task-notification");
            }

            return await SendRuntimeRequestAsync(LidGuardPipeCommands.Start, hookInput);
        }

        if (hookEventName.Equals(ClaudeHookEventNames.Elicitation, StringComparison.Ordinal)) return await WriteClosedLidElicitationDecisionAsync();
        if (hookEventName.Equals(ClaudeHookEventNames.PermissionRequest, StringComparison.Ordinal)) return await WriteClosedLidPermissionRequestDecisionAsync();
        if (ClaudeHookEventNames.IsStopTrigger(hookEventName))
        {
            var isProviderSessionEnd = IsProviderSessionEnd(hookInput);
            if (ClaudeHookWorkTracker.TryCreatePendingWorkReason(hookInput, GetSessionIdentifier(hookInput), out var pendingProviderWorkReason))
            {
                ClaudeHookWorkTracker.RecordDeferredStop(
                    GetSessionIdentifier(hookInput),
                    isProviderSessionEnd,
                    CreateSessionEndReason(hookInput),
                    pendingProviderWorkReason);
                return await SendRuntimeRequestAsync(
                    LidGuardPipeCommands.Stop,
                    hookInput,
                    hasPendingProviderWork: true,
                    pendingProviderWorkReason: pendingProviderWorkReason);
            }

            return await SendRuntimeRequestAsync(LidGuardPipeCommands.Stop, hookInput, isProviderSessionEnd);
        }

        return 0;
    }

    public static int WriteHookSnippet(string format)
    {
        if (string.IsNullOrWhiteSpace(format)) format = SettingsJsonFormat;

        var executablePath = HookCommandUtilities.GetDefaultHookExecutableReference();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            Console.Error.WriteLine("A default LidGuard hook executable or command name could not be resolved.");
            return 1;
        }

        var hookCommand = HookCommandUtilities.CreateHookCommand(executablePath, LidGuardPipeCommands.ClaudeHook);

        if (format.Equals(SettingsJsonFormat, StringComparison.OrdinalIgnoreCase) || format.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(ClaudeHookSettingsJsonDocument.CreateSettingsJsonSnippet(hookCommand));
            return 0;
        }

        if (format.Equals(HooksJsonFormat, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(ClaudeHookSettingsJsonDocument.CreateHooksJsonSnippet(hookCommand));
            return 0;
        }

        Console.Error.WriteLine("Unsupported Claude hook snippet format. Use settings-json or hooks-json.");
        return 1;
    }

    private static async Task<int> WriteClosedLidPermissionRequestDecisionAsync()
    {
        var response = await new LidGuardRuntimeClient().SendAsync(new LidGuardPipeRequest { Command = LidGuardPipeCommands.Status }, false);
        if (!response.Succeeded)
        {
            ClaudeHookEventLog.AppendMessage($"LidGuard Claude hook skipped PermissionRequest decision because runtime status is unavailable: {response.Message}");
            return 0;
        }

        if (!ClosedLidPolicyStatus.IsActive(response))
        {
            ClaudeHookEventLog.AppendMessage(
                $"LidGuard Claude hook left PermissionRequest to Claude because {ClosedLidPolicyStatus.DescribeInactiveReason(response)}.");
            return 0;
        }

        ClaudeHookEventLog.AppendMessage($"LidGuard Claude hook handled closed-lid PermissionRequest with {response.Settings.ClosedLidPermissionRequestDecision}.");
        return ClaudeClosedLidPermissionRequestDecisionOutput.Write(response.Settings);
    }

    private static async Task<int> WriteClosedLidElicitationDecisionAsync()
    {
        var response = await new LidGuardRuntimeClient().SendAsync(new LidGuardPipeRequest { Command = LidGuardPipeCommands.Status }, false);
        if (!response.Succeeded)
        {
            ClaudeHookEventLog.AppendMessage($"LidGuard Claude hook skipped Elicitation decision because runtime status is unavailable: {response.Message}");
            return 0;
        }

        if (!ClosedLidPolicyStatus.IsActive(response))
        {
            ClaudeHookEventLog.AppendMessage(
                $"LidGuard Claude hook left Elicitation to Claude because {ClosedLidPolicyStatus.DescribeInactiveReason(response)}.");
            return 0;
        }

        ClaudeHookEventLog.AppendMessage("LidGuard Claude hook canceled closed-lid Elicitation.");
        return ClaudeClosedLidElicitationOutput.Write();
    }

    private static async Task<int> SendRuntimeRequestAsync(
        string commandName,
        ClaudeHookInput hookInput,
        bool isProviderSessionEnd = false,
        string sessionEndReason = "",
        bool hasPendingProviderWork = false,
        string pendingProviderWorkReason = "")
    {
        // Claude Code hook handling accepts exit 0 + empty stdout as a no-op success,
        // while structured JSON is only needed when a hook intentionally makes a decision.
        var hasSettings = false;
        var settings = LidGuardSettings.Default;
        if (commandName == LidGuardPipeCommands.Start)
        {
            if (!LidGuardSettingsStore.TryLoadOrCreate(out settings, out var settingsMessage))
            {
                ClaudeHookEventLog.AppendMessage(settingsMessage);
                return 0;
            }

            hasSettings = true;
        }

        var request = new LidGuardPipeRequest
        {
            Command = commandName,
            Provider = AgentProvider.Claude,
            SessionIdentifier = GetSessionIdentifier(hookInput),
            IsProviderSessionEnd = isProviderSessionEnd,
            SessionEndReason = isProviderSessionEnd ? CreateSessionEndReason(hookInput, sessionEndReason) : string.Empty,
            HasPendingProviderWork = hasPendingProviderWork,
            PendingProviderWorkReason = pendingProviderWorkReason,
            WatchedProcessIdentifier = HookCommandUtilities.ResolveHookWatchedProcessIdentifier(commandName, AgentProvider.Claude, settings),
            InputPrompt = commandName == LidGuardPipeCommands.Start ? hookInput.Prompt : string.Empty,
            WorkingDirectory = GetWorkingDirectory(hookInput),
            TranscriptPath = hookInput.TranscriptPath,
            HasSettings = hasSettings,
            Settings = settings
        };

        var startRuntimeIfUnavailable = commandName == LidGuardPipeCommands.Start;
        var response = await new LidGuardRuntimeClient().SendAsync(request, startRuntimeIfUnavailable);
        ClaudeHookEventLog.AppendRuntimeResult(hookInput, commandName, response.Succeeded, response.RuntimeUnavailable, response.ActiveSessionCount, response.Message);
        if (commandName == LidGuardPipeCommands.Stop && !hasPendingProviderWork) ClaudeHookWorkTracker.ClearSessionState(GetSessionIdentifier(hookInput));
        return 0;
    }

    private static async Task<int> HandleNotificationAsync(ClaudeHookInput hookInput)
    {
        if (ClaudeSoftLockSignalSource.TryGetSoftLockReason(hookInput, out var softLockReason))
        {
            return await SendSessionStateRequestAsync(LidGuardPipeCommands.MarkSessionSoftLocked, hookInput, softLockReason);
        }

        if (ClaudeSoftLockSignalSource.IsActivityEvent(hookInput)) return await SendSessionStateRequestAsync(LidGuardPipeCommands.MarkSessionActive, hookInput, hookInput.NotificationType);
        return 0;
    }

    private static Task<int> ReportActivityAsync(ClaudeHookInput hookInput)
    {
        if (!ClaudeSoftLockSignalSource.IsActivityEvent(hookInput)) return Task.FromResult(0);
        return SendSessionStateRequestAsync(
            LidGuardPipeCommands.MarkSessionActive,
            hookInput,
            DescribeActivityReason(hookInput.HookEventName, hookInput.ToolName));
    }

    private static Task<int> ReportActivityAsync(ClaudeHookInput hookInput, string activityDetail)
        => SendSessionStateRequestAsync(
            LidGuardPipeCommands.MarkSessionActive,
            hookInput,
            DescribeActivityReason(hookInput.HookEventName, activityDetail));

    private static async Task<int?> TrySendDeferredStopAsync(ClaudeHookInput hookInput)
    {
        var shouldSendDeferredStop = ClaudeHookWorkTracker.TryConsumeDeferredStopWhenNoPendingWork(
            hookInput,
            GetSessionIdentifier(hookInput),
            out var isProviderSessionEnd,
            out var sessionEndReason);
        if (!shouldSendDeferredStop) return null;

        return await SendRuntimeRequestAsync(
            LidGuardPipeCommands.Stop,
            hookInput,
            isProviderSessionEnd,
            sessionEndReason);
    }

    private static async Task<int> SendSessionStateRequestAsync(string commandName, ClaudeHookInput hookInput, string sessionStateReason)
    {
        var request = new LidGuardPipeRequest
        {
            Command = commandName,
            Provider = AgentProvider.Claude,
            SessionIdentifier = GetSessionIdentifier(hookInput),
            SessionStateReason = sessionStateReason,
            WorkingDirectory = GetWorkingDirectory(hookInput),
            TranscriptPath = hookInput.TranscriptPath
        };

        var response = await new LidGuardRuntimeClient().SendAsync(request, false);
        ClaudeHookEventLog.AppendRuntimeResult(hookInput, commandName, response.Succeeded, response.RuntimeUnavailable, response.ActiveSessionCount, response.Message);
        return 0;
    }

    private static string GetSessionIdentifier(ClaudeHookInput hookInput)
    {
        if (!string.IsNullOrWhiteSpace(hookInput.SessionIdentifier)) return hookInput.SessionIdentifier;

        var workingDirectory = GetWorkingDirectory(hookInput);
        var normalizedWorkingDirectory = NormalizeWorkingDirectory(workingDirectory);
        return $"{AgentProvider.Claude}:{normalizedWorkingDirectory}";
    }

    private static string GetWorkingDirectory(ClaudeHookInput hookInput) => string.IsNullOrWhiteSpace(hookInput.WorkingDirectory) ? Environment.CurrentDirectory : hookInput.WorkingDirectory;

    private static bool IsProviderSessionEnd(ClaudeHookInput hookInput)
    {
        if (hookInput.IsInterrupt) return false;

        var hookEventName = hookInput.HookEventName.Trim();
        return hookEventName.Equals(ClaudeHookEventNames.Stop, StringComparison.Ordinal)
            || hookEventName.Equals(ClaudeHookEventNames.SessionEnd, StringComparison.Ordinal);
    }

    private static string CreateSessionEndReason(ClaudeHookInput hookInput, string sessionEndReason = "")
    {
        if (!string.IsNullOrWhiteSpace(sessionEndReason)) return sessionEndReason;
        if (string.IsNullOrWhiteSpace(hookInput.Reason)) return hookInput.HookEventName;
        if (string.IsNullOrWhiteSpace(hookInput.HookEventName)) return hookInput.Reason;
        return $"{hookInput.HookEventName}:{hookInput.Reason}";
    }

    private static string NormalizeWorkingDirectory(string workingDirectory)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(workingDirectory)); }
        catch { return workingDirectory; }
    }

    private static string DescribeActivityReason(string hookEventName, string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName)) return hookEventName;
        return $"{hookEventName}:{toolName}";
    }

}
