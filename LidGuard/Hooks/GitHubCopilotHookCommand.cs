using LidGuard.Ipc;
using LidGuard.Localization;
using LidGuard.Sessions;

namespace LidGuard.Hooks;

internal static class GitHubCopilotHookCommand
{
    private const string ConfigurationJsonFormat = "config-json";
    private const string EventOptionName = "event";
    private const string HooksJsonFormat = "hooks-json";

    public static Task<int> RunAsync(string[] commandLineArguments) => new GitHubCopilotHookCommandRunner().RunAsync(commandLineArguments);

    public static int WriteHookSnippet(string format)
    {
        if (string.IsNullOrWhiteSpace(format)) format = ConfigurationJsonFormat;

        var executablePath = HookCommandUtilities.GetDefaultHookExecutableReference();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            Console.Error.WriteLine(LocalizationService.GetString(
                "HookCommandDefaultExecutableNotResolved",
                "A default LidGuard hook executable or command name could not be resolved."));
            return 1;
        }

        var hookCommand = HookCommandUtilities.CreateHookCommand(executablePath, LidGuardPipeCommands.CopilotHook);
        var hookCommandsByEvent = GitHubCopilotHookConfigurationJsonDocument.CreateManagedHookCommands(hookCommand);

        if (format.Equals(ConfigurationJsonFormat, StringComparison.OrdinalIgnoreCase) || format.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(GitHubCopilotHookConfigurationJsonDocument.CreateConfigurationJson(hookCommandsByEvent));
            return 0;
        }

        if (format.Equals(HooksJsonFormat, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(GitHubCopilotHookConfigurationJsonDocument.CreateHooksJson(hookCommandsByEvent));
            return 0;
        }

        Console.Error.WriteLine(LocalizationService.GetFormattedStringWithFallback(
            "HookCommandUnsupportedSnippetFormat",
            "Unsupported {0} hook snippet format. Use {1}.",
            "GitHub Copilot",
            "config-json or hooks-json"));
        return 1;
    }

    private static bool TryParseConfiguredHookEventName(string[] commandLineArguments, out string configuredHookEventName)
    {
        configuredHookEventName = string.Empty;
        for (var argumentIndex = 0; argumentIndex < commandLineArguments.Length; argumentIndex++)
        {
            var argument = commandLineArguments[argumentIndex];
            if (!argument.StartsWith("--", StringComparison.Ordinal)) continue;

            var separatorIndex = argument.IndexOf('=');
            if (separatorIndex > 2)
            {
                var optionName = argument[2..separatorIndex];
                if (!optionName.Equals(EventOptionName, StringComparison.OrdinalIgnoreCase)) continue;

                configuredHookEventName = argument[(separatorIndex + 1)..].Trim();
                return !string.IsNullOrWhiteSpace(configuredHookEventName);
            }

            var optionNameWithoutValue = argument[2..];
            if (!optionNameWithoutValue.Equals(EventOptionName, StringComparison.OrdinalIgnoreCase)) continue;
            if (argumentIndex + 1 >= commandLineArguments.Length) return false;

            configuredHookEventName = commandLineArguments[argumentIndex + 1].Trim();
            return !string.IsNullOrWhiteSpace(configuredHookEventName);
        }

        return false;
    }

    private sealed class GitHubCopilotHookCommandRunner : HookCommandBase<GitHubCopilotHookInput>
    {
        protected override AgentProvider Provider => AgentProvider.GitHubCopilot;

        public async Task<int> RunAsync(string[] commandLineArguments)
        {
            var timing = new HookExecutionTiming();
            if (!TryParseConfiguredHookEventName(commandLineArguments, out var configuredHookEventName))
            {
                GitHubCopilotHookEventLog.AppendMessage("LidGuard GitHub Copilot hook requires --event <name>.");
                return 0;
            }

            var readResult = await ReadHookInputAsync(
                timing,
                $"LidGuard GitHub Copilot hook received empty input for event '{configuredHookEventName}'.",
                ParseHookInput,
                message => $"LidGuard GitHub Copilot hook could not parse {configuredHookEventName}: {message}");
            if (!readResult.Succeeded) return 0;

            var hookInput = readResult.HookInput;
            timing.AddLogWriteDuration(GitHubCopilotHookEventLog.AppendReceived(configuredHookEventName, hookInput));
            if (configuredHookEventName.Equals(GitHubCopilotHookEventNames.Notification, StringComparison.Ordinal)) return await HandleNotificationAsync(configuredHookEventName, hookInput);
            if (configuredHookEventName.Equals(GitHubCopilotHookEventNames.UserPromptSubmitted, StringComparison.Ordinal)) return await SendRuntimeRequestAsync(LidGuardPipeCommands.Start, configuredHookEventName, hookInput, timing: timing);
            if (configuredHookEventName.Equals(GitHubCopilotHookEventNames.PermissionRequest, StringComparison.Ordinal)) return await WriteClosedLidPermissionRequestDecisionAsync(hookInput);
            if (configuredHookEventName.Equals(GitHubCopilotHookEventNames.SubagentStart, StringComparison.Ordinal))
            {
                GitHubCopilotHookWorkTracker.RecordSubagentStarted(hookInput, GetSessionIdentifier(hookInput));
                return await SendSessionStateRequestAsync(
                    LidGuardPipeCommands.MarkSessionActive,
                    configuredHookEventName,
                    hookInput,
                    DescribeActivityReason(configuredHookEventName, hookInput.AgentName));
            }

            if (configuredHookEventName.Equals(GitHubCopilotHookEventNames.SubagentStop, StringComparison.Ordinal))
            {
                GitHubCopilotHookWorkTracker.RecordSubagentStopped(hookInput, GetSessionIdentifier(hookInput));
                return await SendSessionStateRequestAsync(
                    LidGuardPipeCommands.MarkSessionActive,
                    configuredHookEventName,
                    hookInput,
                    DescribeActivityReason(configuredHookEventName, hookInput.AgentName));
            }

            if (configuredHookEventName.Equals(GitHubCopilotHookEventNames.PreToolUse, StringComparison.Ordinal)) return await HandlePreToolUseAsync(configuredHookEventName, hookInput);
            if (configuredHookEventName.Equals(GitHubCopilotHookEventNames.PostToolUse, StringComparison.Ordinal))
            {
                GitHubCopilotHookWorkTracker.RecordToolUseEvent(hookInput, GetSessionIdentifier(hookInput));
                return await ReportActivityAsync(configuredHookEventName, hookInput, configuredHookEventName);
            }

            if (configuredHookEventName.Equals(GitHubCopilotHookEventNames.AgentStop, StringComparison.Ordinal)
                || configuredHookEventName.Equals(GitHubCopilotHookEventNames.SessionEnd, StringComparison.Ordinal))
            {
                if (GitHubCopilotHookWorkTracker.TryCreatePendingWorkReason(hookInput, GetSessionIdentifier(hookInput), out var pendingProviderWorkReason))
                {
                    GitHubCopilotHookWorkTracker.RecordDeferredStop(
                        GetSessionIdentifier(hookInput),
                        true,
                        CreateSessionEndReason(configuredHookEventName, hookInput, string.Empty),
                        pendingProviderWorkReason);
                    return await SendRuntimeRequestAsync(
                        LidGuardPipeCommands.Stop,
                        configuredHookEventName,
                        hookInput,
                        true,
                        hasPendingProviderWork: true,
                        pendingProviderWorkReason: pendingProviderWorkReason,
                        timing: timing);
                }

                return await SendRuntimeRequestAsync(LidGuardPipeCommands.Stop, configuredHookEventName, hookInput, true, timing: timing);
            }

            return 0;
        }

        protected override void AppendMessage(string message) => GitHubCopilotHookEventLog.AppendMessage(message);

        protected override void AppendRuntimeResult(
            string hookEventName,
            GitHubCopilotHookInput hookInput,
            string commandName,
            LidGuardPipeResponse response,
            string details)
        {
            GitHubCopilotHookEventLog.AppendRuntimeResult(
                hookEventName,
                hookInput,
                commandName,
                response.Succeeded,
                response.RuntimeUnavailable,
                response.ActiveSessionCount,
                response.Message,
                details);
        }

        protected override void ClearSessionState(GitHubCopilotHookInput hookInput) => GitHubCopilotHookWorkTracker.ClearSessionState(GetSessionIdentifier(hookInput));

        protected override string CreateSessionEndReason(string hookEventName, GitHubCopilotHookInput hookInput, string sessionEndReason)
        {
            if (!string.IsNullOrWhiteSpace(sessionEndReason)) return sessionEndReason;

            var detailedReason = hookEventName.Equals(GitHubCopilotHookEventNames.SessionEnd, StringComparison.Ordinal)
                ? hookInput.SessionEndReason
                : hookInput.StopReason;
            if (string.IsNullOrWhiteSpace(detailedReason)) return hookEventName;
            return $"{hookEventName}:{detailedReason}";
        }

        private static HookCommandInputParseResult<GitHubCopilotHookInput> ParseHookInput(string hookInputJson)
        {
            return GitHubCopilotHookInput.TryParse(hookInputJson, out var hookInput, out var message)
                ? HookCommandInputParseResult<GitHubCopilotHookInput>.Success(hookInput)
                : HookCommandInputParseResult<GitHubCopilotHookInput>.Failure(message);
        }

        private Task<int> WriteClosedLidAskUserGuardAsync(GitHubCopilotHookInput hookInput)
        {
            if (!hookInput.ToolName.Equals(GitHubCopilotHookEventNames.AskUserToolName, StringComparison.OrdinalIgnoreCase)) return Task.FromResult(0);

            return WriteClosedLidDecisionAsync(
                response => $"LidGuard GitHub Copilot hook skipped preToolUse ask_user guard because runtime status is unavailable: {response.Message}",
                response => $"LidGuard GitHub Copilot hook left ask_user to Copilot because {ClosedLidPolicyStatus.DescribeInactiveReason(response)}.",
                _ => "LidGuard GitHub Copilot hook denied closed-lid ask_user.",
                _ => GitHubCopilotClosedLidAskUserPreToolUseOutput.Write());
        }

        private Task<int> WriteClosedLidPermissionRequestDecisionAsync(GitHubCopilotHookInput hookInput)
        {
            return WriteClosedLidDecisionAsync(
                response => $"LidGuard GitHub Copilot hook skipped permissionRequest decision because runtime status is unavailable: {response.Message}",
                response => $"LidGuard GitHub Copilot hook left permissionRequest to Copilot because {ClosedLidPolicyStatus.DescribeInactiveReason(response)}.",
                response => $"LidGuard GitHub Copilot hook handled closed-lid permissionRequest for tool '{hookInput.ToolName}' with {response.Settings.ClosedLidPermissionRequestDecision}.",
                response => GitHubCopilotClosedLidPermissionRequestDecisionOutput.Write(response.Settings));
        }

        private async Task<int> HandleNotificationAsync(string configuredHookEventName, GitHubCopilotHookInput hookInput)
        {
            if (GitHubCopilotHookWorkTracker.RecordCompletionNotification(hookInput, GetSessionIdentifier(hookInput)))
            {
                return await SendSessionStateRequestAsync(
                    LidGuardPipeCommands.MarkSessionActive,
                    configuredHookEventName,
                    hookInput,
                    hookInput.NotificationType);
            }

            if (!GitHubCopilotSoftLockSignalSource.TryGetSoftLockReason(configuredHookEventName, hookInput, out var softLockReason)) return 0;
            return await SendSessionStateRequestAsync(
                LidGuardPipeCommands.MarkSessionSoftLocked,
                configuredHookEventName,
                hookInput,
                softLockReason);
        }

        private async Task<int> HandlePreToolUseAsync(string configuredHookEventName, GitHubCopilotHookInput hookInput)
        {
            if (GitHubCopilotSoftLockSignalSource.IsActivityEvent(configuredHookEventName, hookInput))
            {
                await SendSessionStateRequestAsync(
                    LidGuardPipeCommands.MarkSessionActive,
                    configuredHookEventName,
                    hookInput,
                    DescribeActivityReason(configuredHookEventName, hookInput.ToolName));
            }

            return await WriteClosedLidAskUserGuardAsync(hookInput);
        }

        private Task<int> ReportActivityAsync(string configuredHookEventName, GitHubCopilotHookInput hookInput, string sessionStateReason)
        {
            if (!GitHubCopilotSoftLockSignalSource.IsActivityEvent(configuredHookEventName, hookInput)) return Task.FromResult(0);
            return SendSessionStateRequestAsync(
                LidGuardPipeCommands.MarkSessionActive,
                configuredHookEventName,
                hookInput,
                DescribeActivityReason(sessionStateReason, hookInput.ToolName));
        }
    }
}
