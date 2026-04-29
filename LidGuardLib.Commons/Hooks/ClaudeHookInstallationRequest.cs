using LidGuardLib.Commons.Sessions;

namespace LidGuardLib.Commons.Hooks;

public sealed class ClaudeHookInstallationRequest
{
    public AgentProvider Provider { get; init; } = AgentProvider.Claude;

    public string ConfigurationFilePath { get; init; } = string.Empty;

    public string HookExecutablePath { get; init; } = string.Empty;

    public string HookCommandName { get; init; } = "claude-hook";

    public bool CreateBackup { get; init; } = true;
}
