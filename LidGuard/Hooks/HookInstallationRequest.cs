using LidGuard.Sessions;

namespace LidGuard.Hooks;

public sealed class HookInstallationRequest
{
    public AgentProvider Provider { get; init; } = AgentProvider.Unknown;

    public string ConfigurationFilePath { get; init; } = string.Empty;

    public string HookExecutablePath { get; init; } = string.Empty;

    public string HookCommandName { get; init; } = string.Empty;

    public bool CreateBackup { get; init; } = true;
}
