namespace LidGuard.Sessions;

public readonly record struct LidGuardSessionKey
{
    public LidGuardSessionKey(AgentProvider provider, string sessionIdentifier, string providerName = "")
    {
        Provider = provider;
        ProviderName = AgentProviderDisplay.NormalizeProviderName(provider, providerName);
        SessionIdentifier = sessionIdentifier ?? string.Empty;
    }

    public AgentProvider Provider { get; }

    public string ProviderName { get; }

    public string SessionIdentifier { get; }

    public bool IsValid => !string.IsNullOrWhiteSpace(SessionIdentifier);

    public override string ToString() => $"{AgentProviderDisplay.CreateProviderDisplayText(Provider, ProviderName)}:{SessionIdentifier}";
}
