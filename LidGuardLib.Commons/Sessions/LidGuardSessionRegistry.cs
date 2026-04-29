namespace LidGuardLib.Commons.Sessions;

public sealed class LidGuardSessionRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<LidGuardSessionKey, LidGuardSessionSnapshot> _sessions = [];

    public int ActiveSessionCount
    {
        get
        {
            lock (_gate) return _sessions.Count;
        }
    }

    public bool HasActiveSessions
    {
        get
        {
            lock (_gate) return _sessions.Count > 0;
        }
    }

    public LidGuardSessionSnapshot StartOrUpdate(LidGuardSessionStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.SessionIdentifier)) throw new ArgumentException("Session identifier is required.", nameof(request));

        var snapshot = new LidGuardSessionSnapshot
        {
            SessionIdentifier = request.SessionIdentifier,
            Provider = request.Provider,
            StartedAt = request.StartedAt,
            SoftLockState = LidGuardSessionSoftLockState.None,
            SoftLockReason = string.Empty,
            SoftLockedAt = null,
            WatchedProcessIdentifier = request.WatchedProcessIdentifier,
            WorkingDirectory = request.WorkingDirectory
        };

        lock (_gate)
        {
            _sessions[snapshot.Key] = snapshot;
            return snapshot;
        }
    }

    public bool Stop(LidGuardSessionStopRequest request, out LidGuardSessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = new LidGuardSessionKey(request.Provider, request.SessionIdentifier);

        lock (_gate)
        {
            if (_sessions.Remove(key, out snapshot)) return true;
        }

        snapshot = LidGuardSessionSnapshot.Empty;
        return false;
    }

    public bool TryMarkActive(AgentProvider provider, string sessionIdentifier, out LidGuardSessionSnapshot snapshot, out bool changed)
    {
        changed = false;
        snapshot = LidGuardSessionSnapshot.Empty;
        if (string.IsNullOrWhiteSpace(sessionIdentifier)) return false;

        var key = new LidGuardSessionKey(provider, sessionIdentifier);
        lock (_gate)
        {
            if (!_sessions.TryGetValue(key, out var existingSnapshot)) return false;

            if (!existingSnapshot.IsSoftLocked)
            {
                snapshot = existingSnapshot;
                return true;
            }

            snapshot = CloneSnapshot(existingSnapshot, LidGuardSessionSoftLockState.None, string.Empty, null);
            _sessions[key] = snapshot;
            changed = true;
            return true;
        }
    }

    public bool TryMarkSoftLocked(
        AgentProvider provider,
        string sessionIdentifier,
        string softLockReason,
        DateTimeOffset softLockedAt,
        out LidGuardSessionSnapshot snapshot,
        out bool changed)
    {
        changed = false;
        snapshot = LidGuardSessionSnapshot.Empty;
        if (string.IsNullOrWhiteSpace(sessionIdentifier)) return false;

        var key = new LidGuardSessionKey(provider, sessionIdentifier);
        lock (_gate)
        {
            if (!_sessions.TryGetValue(key, out var existingSnapshot)) return false;

            var normalizedSoftLockReason = softLockReason?.Trim() ?? string.Empty;
            if (existingSnapshot.IsSoftLocked && existingSnapshot.SoftLockReason.Equals(normalizedSoftLockReason, StringComparison.Ordinal))
            {
                snapshot = existingSnapshot;
                return true;
            }

            snapshot = CloneSnapshot(existingSnapshot, LidGuardSessionSoftLockState.SoftLocked, normalizedSoftLockReason, softLockedAt);
            _sessions[key] = snapshot;
            changed = true;
            return true;
        }
    }

    public IReadOnlyList<LidGuardSessionSnapshot> GetSnapshots()
    {
        lock (_gate) return [.. _sessions.Values];
    }

    public void Clear()
    {
        lock (_gate) _sessions.Clear();
    }

    private static LidGuardSessionSnapshot CloneSnapshot(
        LidGuardSessionSnapshot snapshot,
        LidGuardSessionSoftLockState softLockState,
        string softLockReason,
        DateTimeOffset? softLockedAt)
    {
        return new LidGuardSessionSnapshot
        {
            SessionIdentifier = snapshot.SessionIdentifier,
            Provider = snapshot.Provider,
            StartedAt = snapshot.StartedAt,
            SoftLockState = softLockState,
            SoftLockReason = softLockReason,
            SoftLockedAt = softLockedAt,
            WatchedProcessIdentifier = snapshot.WatchedProcessIdentifier,
            WorkingDirectory = snapshot.WorkingDirectory
        };
    }
}
