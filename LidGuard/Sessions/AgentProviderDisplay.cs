namespace LidGuard.Sessions;

public static class AgentProviderDisplay
{
    public static string CreateProviderDisplayText(AgentProvider provider, string providerName)
    {
        var normalizedProviderName = NormalizeProviderName(provider, providerName);
        if (provider != AgentProvider.Mcp || string.IsNullOrWhiteSpace(normalizedProviderName)) return provider.ToString();
        return $"{provider}:{normalizedProviderName}";
    }

    public static string NormalizeProviderName(AgentProvider provider, string providerName)
    {
        if (provider != AgentProvider.Mcp) return string.Empty;
        return providerName?.Trim() ?? string.Empty;
    }
}
