using LidGuard.Ipc;
using LidGuard.Settings;
using LidGuardLib.Commons.Hooks;
using LidGuardLib.Commons.Sessions;
using LidGuardLib.Commons.Settings;
using LidGuardLib.Hooks;

namespace LidGuard.Hooks;

internal static class GitHubCopilotHookCommand
{
    private const string ConfigurationJsonFormat = "config-json";
    private const string EventOptionName = "event";
    private const string HooksJsonFormat = "hooks-json";

    public static async Task<int> RunAsync(string[] commandLineArguments)
    {
        if (!TryParseConfiguredHookEventName(commandLineArguments, out var configuredHookEventName))
        {
            GitHubCopilotHookEventLog.AppendMessage("LidGuard GitHub Copilot hook requires --event <name>.");
            return 0;
        }

        var hookInputJson = await Console.In.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(hookInputJson))
        {
            GitHubCopilotHookEventLog.AppendMessage($"LidGuard GitHub Copilot hook received empty input for event '{configuredHookEventName}'.");
            return 0;
        }

        if (!GitHubCopilotHookInput.TryParse(hookInputJson, out var hookInput, out var parseMessage))
        {
            GitHubCopilotHookEventLog.AppendMessage($"LidGuard GitHub Copilot hook could not parse {configuredHookEventName}: {parseMessage}");
            return 0;
        }

        GitHubCopilotHookEventLog.AppendReceived(configuredHookEventName, hookInput);
        if (configuredHookEventName.Equals(GitHubCopilotHookEventNames.Notification, StringComparison.Ordinal)) return await HandleNotificationAsync(configuredHookEventName, hookInput);
        if (configuredHookEventName.Equals(GitHubCopilotHookEventNames.UserPromptSubmitted, StringComparison.Ordinal)) return await SendRuntimeRequestAsync(LidGuardPipeCommands.Start, configuredHookEventName, hookInput);
        if (configuredHookEventName.Equals(GitHubCopilotHookEventNames.PermissionRequest, StringComparison.Ordinal)) return await WriteClosedLidPermissionRequestDecisionAsync(hookInput);
        if (configuredHookEventName.Equals(GitHubCopilotHookEventNames.PreToolUse, StringComparison.Ordinal)) return await HandlePreToolUseAsync(configuredHookEventName, hookInput);
        if (configuredHookEventName.Equals(GitHubCopilotHookEventNames.PostToolUse, StringComparison.Ordinal)) return await ReportActivityAsync(configuredHookEventName, hookInput, configuredHookEventName);
        if (configuredHookEventName.Equals(GitHubCopilotHookEventNames.AgentStop, StringComparison.Ordinal)) return await SendRuntimeRequestAsync(LidGuardPipeCommands.Stop, configuredHookEventName, hookInput, true);
        if (configuredHookEventName.Equals(GitHubCopilotHookEventNames.SessionEnd, StringComparison.Ordinal)) return await SendRuntimeRequestAsync(LidGuardPipeCommands.Stop, configuredHookEventName, hookInput, true);
        return 0;
    }

    public static int WriteHookSnippet(IReadOnlyDictionary<string, string> options)
    {
        var format = GetOption(options, "format");
        if (string.IsNullOrWhiteSpace(format)) format = ConfigurationJsonFormat;

        var executablePath = HookCommandUtilities.GetDefaultHookExecutableReference();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            Console.Error.WriteLine("A default LidGuard hook executable or command name could not be resolved.");
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

        Console.Error.WriteLine("Unsupported GitHub Copilot hook snippet format. Use config-json or hooks-json.");
        return 1;
    }

    private static string GetOption(IReadOnlyDictionary<string, string> options, params string[] optionNames)
    {
        foreach (var optionName in optionNames)
        {
            if (options.TryGetValue(optionName, out var optionValue)) return optionValue;
        }

        return string.Empty;
    }

    private static string GetSessionIdentifier(GitHubCopilotHookInput hookInput)
    {
        if (!string.IsNullOrWhiteSpace(hookInput.SessionIdentifier)) return hookInput.SessionIdentifier;

        var workingDirectory = GetWorkingDirectory(hookInput);
        var normalizedWorkingDirectory = NormalizeWorkingDirectory(workingDirectory);
        return $"{AgentProvider.GitHubCopilot}:{normalizedWorkingDirectory}";
    }

    private static string GetWorkingDirectory(GitHubCopilotHookInput hookInput) => string.IsNullOrWhiteSpace(hookInput.WorkingDirectory) ? Environment.CurrentDirectory : hookInput.WorkingDirectory;

    private static string NormalizeWorkingDirectory(string workingDirectory)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(workingDirectory)); }
        catch { return workingDirectory; }
    }

    private static async Task<int> SendRuntimeRequestAsync(
        string commandName,
        string configuredHookEventName,
        GitHubCopilotHookInput hookInput,
        bool isProviderSessionEnd = false)
    {
        var hasSettings = false;
        var settings = LidGuardSettings.Default;
        if (commandName == LidGuardPipeCommands.Start)
        {
            if (!LidGuardSettingsStore.TryLoadOrCreate(out settings, out var settingsMessage))
            {
                GitHubCopilotHookEventLog.AppendMessage(settingsMessage);
                return 0;
            }

            hasSettings = true;
        }

        var request = new LidGuardPipeRequest
        {
            Command = commandName,
            HasSettings = hasSettings,
            Provider = AgentProvider.GitHubCopilot,
            SessionIdentifier = GetSessionIdentifier(hookInput),
            IsProviderSessionEnd = isProviderSessionEnd,
            SessionEndReason = isProviderSessionEnd ? CreateSessionEndReason(configuredHookEventName, hookInput) : string.Empty,
            Settings = settings,
            TranscriptPath = hookInput.TranscriptPath,
            WorkingDirectory = GetWorkingDirectory(hookInput)
        };

        var startRuntimeIfUnavailable = commandName == LidGuardPipeCommands.Start;
        var response = await new LidGuardRuntimeClient().SendAsync(request, startRuntimeIfUnavailable);
        GitHubCopilotHookEventLog.AppendRuntimeResult(
            configuredHookEventName,
            hookInput,
            commandName,
            response.Succeeded,
            response.RuntimeUnavailable,
            response.ActiveSessionCount,
            response.Message);
        return 0;
    }

    private static string CreateSessionEndReason(string configuredHookEventName, GitHubCopilotHookInput hookInput)
    {
        var detailedReason = configuredHookEventName.Equals(GitHubCopilotHookEventNames.SessionEnd, StringComparison.Ordinal)
            ? hookInput.SessionEndReason
            : hookInput.StopReason;
        if (string.IsNullOrWhiteSpace(detailedReason)) return configuredHookEventName;
        return $"{configuredHookEventName}:{detailedReason}";
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

    private static async Task<int> WriteClosedLidAskUserGuardAsync(GitHubCopilotHookInput hookInput)
    {
        if (!hookInput.ToolName.Equals(GitHubCopilotHookEventNames.AskUserToolName, StringComparison.OrdinalIgnoreCase)) return 0;

        var response = await new LidGuardRuntimeClient().SendAsync(new LidGuardPipeRequest { Command = LidGuardPipeCommands.Status }, false);
        if (!response.Succeeded)
        {
            GitHubCopilotHookEventLog.AppendMessage($"LidGuard GitHub Copilot hook skipped preToolUse ask_user guard because runtime status is unavailable: {response.Message}");
            return 0;
        }

        if (!ClosedLidPolicyStatus.IsActive(response))
        {
            GitHubCopilotHookEventLog.AppendMessage(
                $"LidGuard GitHub Copilot hook left ask_user to Copilot because {ClosedLidPolicyStatus.DescribeInactiveReason(response)}.");
            return 0;
        }

        GitHubCopilotHookEventLog.AppendMessage("LidGuard GitHub Copilot hook denied closed-lid ask_user.");
        return GitHubCopilotClosedLidAskUserPreToolUseOutput.Write();
    }

    private static async Task<int> WriteClosedLidPermissionRequestDecisionAsync(GitHubCopilotHookInput hookInput)
    {
        var response = await new LidGuardRuntimeClient().SendAsync(new LidGuardPipeRequest { Command = LidGuardPipeCommands.Status }, false);
        if (!response.Succeeded)
        {
            GitHubCopilotHookEventLog.AppendMessage($"LidGuard GitHub Copilot hook skipped permissionRequest decision because runtime status is unavailable: {response.Message}");
            return 0;
        }

        if (!ClosedLidPolicyStatus.IsActive(response))
        {
            GitHubCopilotHookEventLog.AppendMessage(
                $"LidGuard GitHub Copilot hook left permissionRequest to Copilot because {ClosedLidPolicyStatus.DescribeInactiveReason(response)}.");
            return 0;
        }

        GitHubCopilotHookEventLog.AppendMessage(
            $"LidGuard GitHub Copilot hook handled closed-lid permissionRequest for tool '{hookInput.ToolName}' with {response.Settings.ClosedLidPermissionRequestDecision}.");
        return GitHubCopilotClosedLidPermissionRequestDecisionOutput.Write(response.Settings);
    }

    private static async Task<int> HandleNotificationAsync(string configuredHookEventName, GitHubCopilotHookInput hookInput)
    {
        if (!GitHubCopilotSoftLockSignalSource.TryGetSoftLockReason(configuredHookEventName, hookInput, out var softLockReason)) return 0;
        return await SendSessionStateRequestAsync(
            LidGuardPipeCommands.MarkSessionSoftLocked,
            configuredHookEventName,
            hookInput,
            softLockReason);
    }

    private static async Task<int> HandlePreToolUseAsync(string configuredHookEventName, GitHubCopilotHookInput hookInput)
    {
        if (GitHubCopilotSoftLockSignalSource.IsActivityEvent(configuredHookEventName, hookInput)) await SendSessionStateRequestAsync(LidGuardPipeCommands.MarkSessionActive, configuredHookEventName, hookInput, DescribeActivityReason(configuredHookEventName, hookInput.ToolName));

        return await WriteClosedLidAskUserGuardAsync(hookInput);
    }

    private static Task<int> ReportActivityAsync(string configuredHookEventName, GitHubCopilotHookInput hookInput, string sessionStateReason)
    {
        if (!GitHubCopilotSoftLockSignalSource.IsActivityEvent(configuredHookEventName, hookInput)) return Task.FromResult(0);
        return SendSessionStateRequestAsync(
            LidGuardPipeCommands.MarkSessionActive,
            configuredHookEventName,
            hookInput,
            DescribeActivityReason(sessionStateReason, hookInput.ToolName));
    }

    private static async Task<int> SendSessionStateRequestAsync(
        string commandName,
        string configuredHookEventName,
        GitHubCopilotHookInput hookInput,
        string sessionStateReason)
    {
        var request = new LidGuardPipeRequest
        {
            Command = commandName,
            Provider = AgentProvider.GitHubCopilot,
            SessionIdentifier = GetSessionIdentifier(hookInput),
            SessionStateReason = sessionStateReason,
            TranscriptPath = hookInput.TranscriptPath,
            WorkingDirectory = GetWorkingDirectory(hookInput)
        };

        var response = await new LidGuardRuntimeClient().SendAsync(request, false);
        GitHubCopilotHookEventLog.AppendRuntimeResult(
            configuredHookEventName,
            hookInput,
            commandName,
            response.Succeeded,
            response.RuntimeUnavailable,
            response.ActiveSessionCount,
            response.Message);
        return 0;
    }

    private static string DescribeActivityReason(string hookEventName, string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName)) return hookEventName;
        return $"{hookEventName}:{toolName}";
    }
}
