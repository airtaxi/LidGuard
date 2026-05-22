using LidGuard.Sessions;

namespace LidGuard.Commands;

internal static class WslProviderConfigurationRoots
{
    public static bool TryGetHookConfigurationFilePath(
        string distroName,
        AgentProvider provider,
        string configuredConfigurationFilePath,
        out string configurationFilePath,
        out string message)
    {
        if (!string.IsNullOrWhiteSpace(configuredConfigurationFilePath)) return WslCommandUtilities.TryNormalizeWslPath(distroName, configuredConfigurationFilePath, out configurationFilePath, out message);

        return provider switch
        {
            AgentProvider.Codex => WslCommandUtilities.TryResolveDefaultPath(distroName, "printf '%s' \"${CODEX_HOME:-$HOME/.codex}/config.toml\"", out configurationFilePath, out message),
            AgentProvider.Claude => WslCommandUtilities.TryResolveDefaultPath(distroName, "printf '%s' \"${CLAUDE_CONFIG_DIR:-$HOME/.claude}/settings.json\"", out configurationFilePath, out message),
            AgentProvider.GitHubCopilot => WslCommandUtilities.TryResolveDefaultPath(distroName, "printf '%s' \"${COPILOT_HOME:-$HOME/.copilot}/hooks/lidguard-copilot-cli.json\"", out configurationFilePath, out message),
            _ => UnsupportedProvider(out configurationFilePath, out message)
        };
    }

    public static bool TryGetMcpConfigurationFilePath(
        string distroName,
        AgentProvider provider,
        out string configurationFilePath,
        out string message)
    {
        return provider switch
        {
            AgentProvider.Codex => WslCommandUtilities.TryResolveDefaultPath(distroName, "printf '%s' \"${CODEX_HOME:-$HOME/.codex}/config.toml\"", out configurationFilePath, out message),
            AgentProvider.Claude => WslCommandUtilities.TryResolveDefaultPath(distroName, "printf '%s' \"$HOME/.claude.json\"", out configurationFilePath, out message),
            AgentProvider.GitHubCopilot => WslCommandUtilities.TryResolveDefaultPath(distroName, "printf '%s' \"${COPILOT_HOME:-$HOME/.copilot}/mcp-config.json\"", out configurationFilePath, out message),
            _ => UnsupportedProvider(out configurationFilePath, out message)
        };
    }

    public static bool TryGetHookCandidatePaths(string distroName, AgentProvider provider, out IReadOnlyList<string> candidatePaths, out string message)
    {
        candidatePaths = [];
        message = string.Empty;

        return provider switch
        {
            AgentProvider.Codex => TryResolveCandidatePaths(distroName, "printf '%s\n%s' \"${CODEX_HOME:-$HOME/.codex}\" \"${CODEX_HOME:-$HOME/.codex}/config.toml\"", out candidatePaths, out message),
            AgentProvider.Claude => TryResolveCandidatePaths(distroName, "printf '%s\n%s' \"${CLAUDE_CONFIG_DIR:-$HOME/.claude}\" \"${CLAUDE_CONFIG_DIR:-$HOME/.claude}/settings.json\"", out candidatePaths, out message),
            AgentProvider.GitHubCopilot => TryResolveCandidatePaths(distroName, "printf '%s\n%s\n%s' \"${COPILOT_HOME:-$HOME/.copilot}\" \"$PWD/.github/hooks\" \"$PWD/.github/copilot\"", out candidatePaths, out message),
            _ => UnsupportedProvider(out candidatePaths, out message)
        };
    }

    public static bool TryGetMcpCandidatePaths(string distroName, AgentProvider provider, out IReadOnlyList<string> candidatePaths, out string message)
    {
        candidatePaths = [];
        message = string.Empty;

        return provider switch
        {
            AgentProvider.Codex => TryResolveCandidatePaths(distroName, "printf '%s\n%s' \"${CODEX_HOME:-$HOME/.codex}\" \"${CODEX_HOME:-$HOME/.codex}/config.toml\"", out candidatePaths, out message),
            AgentProvider.Claude => TryResolveCandidatePaths(distroName, "printf '%s\n%s' \"$HOME/.claude.json\" \"${CLAUDE_CONFIG_DIR:-$HOME/.claude}\"", out candidatePaths, out message),
            AgentProvider.GitHubCopilot => TryResolveCandidatePaths(distroName, "printf '%s\n%s' \"${COPILOT_HOME:-$HOME/.copilot}/mcp-config.json\" \"${COPILOT_HOME:-$HOME/.copilot}\"", out candidatePaths, out message),
            _ => UnsupportedProvider(out candidatePaths, out message)
        };
    }

    private static bool TryResolveCandidatePaths(string distroName, string script, out IReadOnlyList<string> candidatePaths, out string message)
    {
        candidatePaths = [];
        var result = WslCommandUtilities.RunShell(distroName, script, []);
        if (result.ExitCode != 0)
        {
            message = result.GetDisplayError();
            return false;
        }

        candidatePaths = result.StandardOutput
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        message = string.Empty;
        return true;
    }

    private static bool UnsupportedProvider(out string configurationFilePath, out string message)
    {
        configurationFilePath = string.Empty;
        message = string.Empty;
        return false;
    }

    private static bool UnsupportedProvider(out IReadOnlyList<string> candidatePaths, out string message)
    {
        candidatePaths = [];
        message = string.Empty;
        return false;
    }
}
