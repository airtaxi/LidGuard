using LidGuard.Sessions;

namespace LidGuard.Hooks;

public sealed class HookInstallationInspection
{
    public AgentProvider Provider { get; init; } = AgentProvider.Unknown;

    public HookInstallationStatus Status { get; init; } = HookInstallationStatus.Unknown;

    public string ConfigurationFilePath { get; init; } = string.Empty;

    public string HookExecutablePath { get; init; } = string.Empty;

    public string HookCommand { get; init; } = string.Empty;

    public bool ConfigurationFileExists { get; init; }

    public IReadOnlyDictionary<HookInstallationCheck, bool> Checks { get; init; } = new Dictionary<HookInstallationCheck, bool>();

    public IReadOnlyList<string> ConflictingAgentStopHookSources { get; init; } = [];

    public string Message { get; init; } = string.Empty;

    public bool IsInstalled => Status == HookInstallationStatus.Installed;

    public bool HasCheck(HookInstallationCheck check) => Checks.TryGetValue(check, out var isPresent) && isPresent;

    public HookInstallationInspection WithConflictingAgentStopHookSources(IReadOnlyList<string> conflictingAgentStopHookSources)
    {
        var checks = new Dictionary<HookInstallationCheck, bool>(Checks)
        {
            [HookInstallationCheck.ConflictingAgentStopHooks] = conflictingAgentStopHookSources.Count > 0
        };

        return new HookInstallationInspection
        {
            Provider = Provider,
            Status = Status,
            ConfigurationFilePath = ConfigurationFilePath,
            HookExecutablePath = HookExecutablePath,
            HookCommand = HookCommand,
            ConfigurationFileExists = ConfigurationFileExists,
            Checks = checks,
            ConflictingAgentStopHookSources = conflictingAgentStopHookSources,
            Message = Message
        };
    }
}
