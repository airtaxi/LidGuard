using LidGuardLib.Commons.Sessions;

namespace LidGuard.Runtime;

internal sealed class LidGuardRuntimeSessionLogEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public string EventName { get; init; } = string.Empty;

    public string Command { get; init; } = string.Empty;

    public AgentProvider Provider { get; init; } = AgentProvider.Unknown;

    public string ProviderName { get; init; } = string.Empty;

    public string SessionIdentifier { get; init; } = string.Empty;

    public LidGuardSessionSoftLockState SoftLockState { get; init; }

    public string SoftLockReason { get; init; } = string.Empty;

    public DateTimeOffset? SoftLockedAt { get; init; }

    public int WatchedProcessIdentifier { get; init; }

    public string WorkingDirectory { get; init; } = string.Empty;

    public bool Succeeded { get; init; }

    public string Message { get; init; } = string.Empty;

    public int ActiveSessionCount { get; init; }
}

