using LidGuardLib.Commons.Sessions;
using LidGuard.Ipc;

namespace LidGuard.Control;

public sealed class LidGuardSessionRemovalOutcome
{
    public string RequestedSessionIdentifier { get; init; } = string.Empty;

    public bool HasProviderFilter { get; init; }

    public AgentProvider RequestedProvider { get; init; } = AgentProvider.Unknown;

    public LidGuardSessionStatus[] RemovedSessions { get; init; } = [];

    public LidGuardControlSnapshot Snapshot { get; init; } = new();
}
