using LidGuardLib.Commons.Sessions;

namespace LidGuard.Ipc;

public sealed class LidGuardSessionStatus
{
    public required string SessionIdentifier { get; init; }

    public AgentProvider Provider { get; init; } = AgentProvider.Unknown;

    public DateTimeOffset StartedAt { get; init; }

    public int WatchedProcessIdentifier { get; init; }

    public string WorkingDirectory { get; init; } = string.Empty;
}
