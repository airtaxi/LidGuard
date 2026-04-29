using LidGuardLib.Commons.Sessions;

namespace LidGuardLib.Commons.Hooks;

public sealed class CodexHookInstallationInspection
{
    public AgentProvider Provider { get; init; } = AgentProvider.Unknown;

    public CodexHookConfigurationFormat Format { get; init; } = CodexHookConfigurationFormat.ConfigToml;

    public CodexHookInstallationStatus Status { get; init; } = CodexHookInstallationStatus.Unknown;

    public string ConfigurationFilePath { get; init; } = string.Empty;

    public string HookExecutablePath { get; init; } = string.Empty;

    public string HookCommand { get; init; } = string.Empty;

    public bool ConfigurationFileExists { get; init; }

    public bool HasCodexHooksFeatureFlag { get; init; }

    public bool HasManagedBlock { get; init; }

    public bool HasUserPromptSubmitHook { get; init; }

    public bool HasPermissionRequestHook { get; init; }

    public bool HasSessionEndHook { get; init; }

    public bool HasStopHook { get; init; }

    public bool HasExpectedHookCommand { get; init; }

    public bool HasValidHookCommand { get; init; }

    public string Message { get; init; } = string.Empty;

    public bool HasAllStopHooks => HasStopHook && HasSessionEndHook;

    public bool IsInstalled => Status == CodexHookInstallationStatus.Installed;
}
