namespace LidGuard.Sessions;

public sealed class LidGuardSessionStopRequest
{
    public required string SessionIdentifier { get; init; }

    public AgentProvider Provider { get; init; } = AgentProvider.Unknown;

    public string ProviderName { get; init; } = string.Empty;

    public bool IsProviderSessionEnd { get; init; }

    public string SessionEndReason { get; init; } = string.Empty;

    public bool HasPendingProviderWork { get; init; }

    public string PendingProviderWorkReason { get; init; } = string.Empty;

    public bool SuppressWebhooks { get; init; }

    public string LastAssistantMessage { get; init; } = string.Empty;
}
