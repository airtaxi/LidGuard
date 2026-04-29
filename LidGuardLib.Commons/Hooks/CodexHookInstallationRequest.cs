using LidGuardLib.Commons.Sessions;

namespace LidGuardLib.Commons.Hooks;

public sealed class CodexHookInstallationRequest
{
    public AgentProvider Provider { get; init; } = AgentProvider.Codex;

    public CodexHookConfigurationFormat Format { get; init; } = CodexHookConfigurationFormat.ConfigToml;

    public string ConfigurationFilePath { get; init; } = string.Empty;

    public string HookExecutablePath { get; init; } = string.Empty;

    public string HookCommandName { get; init; } = "codex-hook";

    public bool CreateBackup { get; init; } = true;
}
