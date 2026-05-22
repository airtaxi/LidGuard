using System.Text.Json;
using LidGuard.Ipc;
using LidGuard.Localization;
using LidGuard.Sessions;

namespace LidGuard.Hooks;

internal static class ClaudeHookCommand
{
    private const string HooksJsonFormat = "hooks-json";
    private const string SettingsJsonFormat = "settings-json";

    public static Task<int> RunAsync() => new ClaudeHookCommandRunner().RunAsync();

    public static int WriteHookSnippet(string format)
    {
        if (string.IsNullOrWhiteSpace(format)) format = SettingsJsonFormat;

        var executablePath = HookCommandUtilities.GetDefaultHookExecutableReference();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            Console.Error.WriteLine(LocalizationService.GetString("HookCommandDefaultExecutableNotResolved"));
            return 1;
        }

        var hookCommand = HookCommandUtilities.CreateHookCommand(executablePath, LidGuardPipeCommands.ClaudeHook);
        return WriteHookSnippet(format, hookCommand, HookCommandUtilities.GetCommandHookShellNameForCurrentPlatform());
    }

    internal static int WriteHookSnippet(string format, string hookCommand, string hookShellName)
    {
        if (string.IsNullOrWhiteSpace(format)) format = SettingsJsonFormat;
        if (format.Equals(SettingsJsonFormat, StringComparison.OrdinalIgnoreCase) || format.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(ClaudeHookSettingsJsonDocument.CreateSettingsJsonSnippet(hookCommand, hookShellName));
            return 0;
        }

        if (format.Equals(HooksJsonFormat, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(ClaudeHookSettingsJsonDocument.CreateHooksJsonSnippet(hookCommand, hookShellName));
            return 0;
        }

        Console.Error.WriteLine(LocalizationService.GetFormattedString("HookCommandUnsupportedSnippetFormat", "Claude", "settings-json or hooks-json"));
        return 1;
    }

    private sealed class ClaudeHookCommandRunner : HookCommandBase<ClaudeHookInput>
    {
        protected override AgentProvider Provider => AgentProvider.Claude;

        public async Task<int> RunAsync()
        {
            var timing = new HookExecutionTiming();
            var readResult = await ReadHookInputAsync(timing, "LidGuard Claude hook received empty input.", ParseHookInput, message => message);
            if (!readResult.Succeeded) return 0;

            var hookInput = readResult.HookInput;
            timing.AddLogWriteDuration(ClaudeHookEventLog.AppendReceived(hookInput));
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
                return await ReportActivityAsync(hookInput, hookInput.TaskIdentifier);
            }

            if (hookEventName.Equals(ClaudeHookEventNames.PreToolUse, StringComparison.Ordinal)
                || hookEventName.Equals(ClaudeHookEventNames.PostToolUse, StringComparison.Ordinal))
            {
                ClaudeHookWorkTracker.RecordToolUseEvent(hookInput, GetSessionIdentifier(hookInput));
                return await ReportActivityAsync(hookInput);
            }

            if (hookEventName.Equals(ClaudeHookEventNames.PostToolUseFailure, StringComparison.Ordinal))
            {
                return hookInput.IsInterrupt ? await SendRuntimeRequestAsync(LidGuardPipeCommands.Stop, hookEventName, hookInput, timing: timing) : await ReportActivityAsync(hookInput);
            }

            if (hookEventName.Equals(ClaudeHookEventNames.UserPromptSubmit, StringComparison.Ordinal))
            {
                if (ClaudeHookWorkTracker.TryRecordTaskNotification(hookInput, GetSessionIdentifier(hookInput))) return await ReportActivityAsync(hookInput, "task-notification");

                return await SendRuntimeRequestAsync(LidGuardPipeCommands.Start, hookEventName, hookInput, timing: timing);
            }

            if (hookEventName.Equals(ClaudeHookEventNames.Elicitation, StringComparison.Ordinal)) return await WriteClosedLidElicitationDecisionAsync(hookInput);
            if (hookEventName.Equals(ClaudeHookEventNames.PermissionRequest, StringComparison.Ordinal)) return await WriteClosedLidPermissionRequestDecisionAsync(hookInput);
            if (ClaudeHookEventNames.IsStopTrigger(hookEventName))
            {
                var isProviderSessionEnd = IsProviderSessionEnd(hookInput);
                if (ClaudeHookWorkTracker.TryCreatePendingWorkReason(hookInput, GetSessionIdentifier(hookInput), out var pendingProviderWorkReason))
                {
                    ClaudeHookWorkTracker.RecordDeferredStop(GetSessionIdentifier(hookInput), isProviderSessionEnd, CreateSessionEndReason(hookEventName, hookInput, string.Empty), pendingProviderWorkReason);
                    return await SendRuntimeRequestAsync(LidGuardPipeCommands.Stop, hookEventName, hookInput, hasPendingProviderWork: true, pendingProviderWorkReason: pendingProviderWorkReason, timing: timing);
                }

                return await SendRuntimeRequestAsync(LidGuardPipeCommands.Stop, hookEventName, hookInput, isProviderSessionEnd, timing: timing);
            }

            return 0;
        }

        protected override void AppendMessage(string message) => ClaudeHookEventLog.AppendMessage(message);

        protected override void AppendRuntimeResult(string hookEventName, ClaudeHookInput hookInput, string commandName, LidGuardPipeResponse response, string details)
        {
            ClaudeHookEventLog.AppendRuntimeResult(hookInput, commandName, response.Succeeded, response.RuntimeUnavailable, response.ActiveSessionCount, response.Message, details);
        }

        protected override void ClearSessionState(ClaudeHookInput hookInput) => ClaudeHookWorkTracker.ClearSessionState(GetSessionIdentifier(hookInput));

        protected override string CreateSessionEndReason(string hookEventName, ClaudeHookInput hookInput, string sessionEndReason)
        {
            if (!string.IsNullOrWhiteSpace(sessionEndReason)) return sessionEndReason;
            if (string.IsNullOrWhiteSpace(hookInput.Reason)) return hookInput.HookEventName;
            if (string.IsNullOrWhiteSpace(hookInput.HookEventName)) return hookInput.Reason;
            return $"{hookInput.HookEventName}:{hookInput.Reason}";
        }

        protected override bool CanReturnStopContinuation(string hookEventName, ClaudeHookInput hookInput)
            => hookEventName.Equals(ClaudeHookEventNames.Stop, StringComparison.Ordinal);

        protected override string GetLastAssistantMessage(ClaudeHookInput hookInput) => hookInput.LastAssistantMessage;

        protected override bool IsStopHookAlreadyActive(ClaudeHookInput hookInput) => hookInput.StopHookActive;

        private static HookCommandInputParseResult<ClaudeHookInput> ParseHookInput(string hookInputJson)
        {
            try
            {
                var hookInput = JsonSerializer.Deserialize(hookInputJson, LidGuardJsonSerializerContext.Default.ClaudeHookInput);
                return hookInput is null ? HookCommandInputParseResult<ClaudeHookInput>.Failure("LidGuard Claude hook could not parse input.") : HookCommandInputParseResult<ClaudeHookInput>.Success(hookInput);
            }
            catch (JsonException exception)
            {
                return HookCommandInputParseResult<ClaudeHookInput>.Failure($"LidGuard Claude hook could not parse input: {exception.Message}");
            }
        }

        private Task<int> WriteClosedLidPermissionRequestDecisionAsync(ClaudeHookInput hookInput)
        {
            return WriteClosedLidDecisionAsync(ClaudeHookEventNames.PermissionRequest, hookInput, response => $"LidGuard Claude hook skipped PermissionRequest decision because runtime status is unavailable: {response.Message}", response => $"LidGuard Claude hook left PermissionRequest to Claude because {ClosedLidPolicyStatus.DescribeInactiveReason(response)}.", response => $"LidGuard Claude hook handled closed-lid PermissionRequest with {response.Settings.ClosedLidPermissionRequestDecision}.", response => ClaudeClosedLidPermissionRequestDecisionOutput.Write(response.Settings), true);
        }

        private Task<int> WriteClosedLidElicitationDecisionAsync(ClaudeHookInput hookInput)
        {
            return WriteClosedLidDecisionAsync(ClaudeHookEventNames.Elicitation, hookInput, response => $"LidGuard Claude hook skipped Elicitation decision because runtime status is unavailable: {response.Message}", response => $"LidGuard Claude hook left Elicitation to Claude because {ClosedLidPolicyStatus.DescribeInactiveReason(response)}.", _ => "LidGuard Claude hook canceled closed-lid Elicitation.", _ => ClaudeClosedLidElicitationOutput.Write());
        }

        private async Task<int> HandleNotificationAsync(ClaudeHookInput hookInput)
        {
            if (ClaudeSoftLockSignalSource.TryGetSoftLockReason(hookInput, out var softLockReason)) return await SendSessionStateRequestAsync(LidGuardPipeCommands.MarkSessionSoftLocked, hookInput.HookEventName, hookInput, softLockReason);

            if (ClaudeSoftLockSignalSource.IsActivityEvent(hookInput)) return await SendSessionStateRequestAsync(LidGuardPipeCommands.MarkSessionActive, hookInput.HookEventName, hookInput, hookInput.NotificationType);
            return 0;
        }

        private Task<int> ReportActivityAsync(ClaudeHookInput hookInput)
        {
            if (!ClaudeSoftLockSignalSource.IsActivityEvent(hookInput)) return Task.FromResult(0);
            return SendSessionStateRequestAsync(LidGuardPipeCommands.MarkSessionActive, hookInput.HookEventName, hookInput, DescribeActivityReason(hookInput.HookEventName, hookInput.ToolName));
        }

        private Task<int> ReportActivityAsync(ClaudeHookInput hookInput, string activityDetail)
            => SendSessionStateRequestAsync(LidGuardPipeCommands.MarkSessionActive, hookInput.HookEventName, hookInput, DescribeActivityReason(hookInput.HookEventName, activityDetail));

        private static bool IsProviderSessionEnd(ClaudeHookInput hookInput)
        {
            if (hookInput.IsInterrupt) return false;

            var hookEventName = hookInput.HookEventName.Trim();
            return hookEventName.Equals(ClaudeHookEventNames.Stop, StringComparison.Ordinal)
                || hookEventName.Equals(ClaudeHookEventNames.SessionEnd, StringComparison.Ordinal);
        }
    }
}
