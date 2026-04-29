namespace LidGuardLib.Commons.Sessions;

public sealed class LidGuardSessionStopRequest
{
    public required string SessionIdentifier { get; init; }

    public AgentProvider Provider { get; init; } = AgentProvider.Unknown;

    public string ProviderName { get; init; } = string.Empty;
}
