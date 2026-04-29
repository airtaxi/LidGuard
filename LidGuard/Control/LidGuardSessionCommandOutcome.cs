using LidGuardLib.Commons.Sessions;

namespace LidGuard.Control;

public sealed class LidGuardSessionCommandOutcome
{
    public string RequestedCommand { get; init; } = string.Empty;

    public string RequestedSessionIdentifier { get; init; } = string.Empty;

    public AgentProvider RequestedProvider { get; init; } = AgentProvider.Unknown;

    public string RequestedProviderName { get; init; } = string.Empty;

    public string RuntimeMessage { get; init; } = string.Empty;

    public LidGuardControlSnapshot Snapshot { get; init; } = new();
}
