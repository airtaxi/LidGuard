using LidGuard.Sessions;

namespace LidGuard.Ipc;

public sealed class LidGuardSessionStatus
{
    public required string SessionIdentifier { get; init; }

    public AgentProvider Provider { get; init; } = AgentProvider.Unknown;

    public string ProviderName { get; init; } = string.Empty;

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset LastActivityAt { get; init; }

    public LidGuardSessionSoftLockState SoftLockState { get; init; }

    public string SoftLockReason { get; init; } = string.Empty;

    public DateTimeOffset? SoftLockedAt { get; init; }

    public int WatchedProcessIdentifier { get; init; }

    public string WorkingDirectory { get; init; } = string.Empty;
}
