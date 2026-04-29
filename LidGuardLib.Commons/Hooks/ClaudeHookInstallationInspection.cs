using LidGuardLib.Commons.Sessions;

namespace LidGuardLib.Commons.Hooks;

public sealed class ClaudeHookInstallationInspection
{
    public AgentProvider Provider { get; init; } = AgentProvider.Claude;

    public CodexHookInstallationStatus Status { get; init; } = CodexHookInstallationStatus.Unknown;

    public string ConfigurationFilePath { get; init; } = string.Empty;

    public string HookExecutablePath { get; init; } = string.Empty;

    public string HookCommand { get; init; } = string.Empty;

    public bool ConfigurationFileExists { get; init; }

    public bool HasHooksObject { get; init; }

    public bool HasManagedHookEntries { get; init; }

    public bool HasExpectedHookCommand { get; init; }

    public bool HasExpectedHookShell { get; init; }

    public bool HasUserPromptSubmitHook { get; init; }

    public bool HasStopHook { get; init; }

    public bool HasStopFailureHook { get; init; }

    public bool HasElicitationHook { get; init; }

    public bool HasPermissionRequestHook { get; init; }

    public bool HasSessionEndHook { get; init; }

    public string Message { get; init; } = string.Empty;

    public bool HasAllStopHooks => HasStopHook && HasStopFailureHook && HasSessionEndHook;

    public bool IsInstalled => Status == CodexHookInstallationStatus.Installed;
}
