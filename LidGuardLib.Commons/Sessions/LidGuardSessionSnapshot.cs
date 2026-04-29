namespace LidGuardLib.Commons.Sessions;

public sealed class LidGuardSessionSnapshot
{
    public static LidGuardSessionSnapshot Empty { get; } = new()
    {
        SessionIdentifier = string.Empty,
        Provider = AgentProvider.Unknown,
        StartedAt = DateTimeOffset.MinValue,
        SoftLockState = LidGuardSessionSoftLockState.None,
        SoftLockReason = string.Empty,
        SoftLockedAt = null,
        WatchedProcessIdentifier = 0,
        WorkingDirectory = string.Empty
    };

    public required string SessionIdentifier { get; init; }

    public AgentProvider Provider { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public LidGuardSessionSoftLockState SoftLockState { get; init; }

    public string SoftLockReason { get; init; } = string.Empty;

    public DateTimeOffset? SoftLockedAt { get; init; }

    public int WatchedProcessIdentifier { get; init; }

    public string WorkingDirectory { get; init; } = string.Empty;

    public LidGuardSessionKey Key => new(Provider, SessionIdentifier);

    public bool HasWatchedProcess => WatchedProcessIdentifier > 0;

    public bool IsSoftLocked => SoftLockState == LidGuardSessionSoftLockState.SoftLocked;
}
