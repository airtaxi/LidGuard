using LidGuard.Sessions;

namespace LidGuard.Commands;

internal static class AgentProviderOptionParser
{
    public static bool TryParseProvider(string providerText, out AgentProvider provider)
    {
        provider = AgentProvider.Unknown;
        if (string.IsNullOrWhiteSpace(providerText)) return false;

        provider = providerText.Trim().ToLowerInvariant() switch
        {
            "codex" => AgentProvider.Codex,
            "claude" => AgentProvider.Claude,
            "copilot" or "github-copilot" or "githubcopilot" => AgentProvider.GitHubCopilot,
            "custom" => AgentProvider.Custom,
            "mcp" => AgentProvider.Mcp,
            "unknown" => AgentProvider.Unknown,
            _ => AgentProvider.Unknown
        };

        return provider != AgentProvider.Unknown || providerText.Equals("unknown", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetSessionProviderName(IReadOnlyDictionary<string, string> options, AgentProvider provider)
    {
        if (provider != AgentProvider.Mcp) return string.Empty;
        return CommandOptionReader.GetOption(options, "provider-name", "mcp-provider-name").Trim();
    }
}
