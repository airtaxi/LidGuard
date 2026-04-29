using LidGuardLib.Commons.Sessions;

namespace LidGuardLib.Commons.Hooks;

public sealed class GitHubCopilotHookInstallationInspection
{
    public string ConfigurationFilePath { get; init; } = string.Empty;

    public bool ConfigurationFileExists { get; init; }

    public IReadOnlyList<string> ConflictingAgentStopHookSources { get; init; } = [];

    public bool HasAgentStopHook { get; init; }

    public bool HasConflictingAgentStopHooks { get; init; }

    public bool HasErrorOccurredHook { get; init; }

    public bool HasExpectedHookCommands { get; init; }

    public bool HasExpectedNotificationMatcher { get; init; }

    public bool HasHooksObject { get; init; }

    public bool HasManagedHookEntries { get; init; }

    public bool HasNotificationHook { get; init; }

    public bool HasPermissionRequestHook { get; init; }

    public bool HasPostToolUseHook { get; init; }

    public bool HasPreToolUseHook { get; init; }

    public bool HasSessionEndHook { get; init; }

    public bool HasSessionStartHook { get; init; }

    public bool HasUserPromptSubmittedHook { get; init; }

    public string HookCommand { get; init; } = string.Empty;

    public string HookExecutablePath { get; init; } = string.Empty;

    public bool IsInstalled => Status == CodexHookInstallationStatus.Installed;

    public string Message { get; init; } = string.Empty;

    public AgentProvider Provider { get; init; } = AgentProvider.GitHubCopilot;

    public CodexHookInstallationStatus Status { get; init; } = CodexHookInstallationStatus.Unknown;

    public GitHubCopilotHookInstallationInspection WithConflictingAgentStopHookSources(IReadOnlyList<string> conflictingAgentStopHookSources)
    {
        return new GitHubCopilotHookInstallationInspection
        {
            ConfigurationFilePath = ConfigurationFilePath,
            ConfigurationFileExists = ConfigurationFileExists,
            ConflictingAgentStopHookSources = conflictingAgentStopHookSources,
            HasAgentStopHook = HasAgentStopHook,
            HasConflictingAgentStopHooks = conflictingAgentStopHookSources.Count > 0,
            HasErrorOccurredHook = HasErrorOccurredHook,
            HasExpectedHookCommands = HasExpectedHookCommands,
            HasExpectedNotificationMatcher = HasExpectedNotificationMatcher,
            HasHooksObject = HasHooksObject,
            HasManagedHookEntries = HasManagedHookEntries,
            HasNotificationHook = HasNotificationHook,
            HasPermissionRequestHook = HasPermissionRequestHook,
            HasPostToolUseHook = HasPostToolUseHook,
            HasPreToolUseHook = HasPreToolUseHook,
            HasSessionEndHook = HasSessionEndHook,
            HasSessionStartHook = HasSessionStartHook,
            HasUserPromptSubmittedHook = HasUserPromptSubmittedHook,
            HookCommand = HookCommand,
            HookExecutablePath = HookExecutablePath,
            Message = Message,
            Provider = Provider,
            Status = Status
        };
    }
}
