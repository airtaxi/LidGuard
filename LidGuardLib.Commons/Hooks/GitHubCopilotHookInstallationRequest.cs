using LidGuardLib.Commons.Sessions;

namespace LidGuardLib.Commons.Hooks;

public sealed class GitHubCopilotHookInstallationRequest
{
    public string ConfigurationFilePath { get; init; } = string.Empty;

    public bool CreateBackup { get; init; } = true;

    public string HookCommandName { get; init; } = "copilot-hook";

    public string HookExecutablePath { get; init; } = string.Empty;

    public AgentProvider Provider { get; init; } = AgentProvider.GitHubCopilot;
}
