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
            ProviderName = AgentProviderDisplay.NormalizeProviderName(request.Provider, request.ProviderName),
            StartedAt = request.StartedAt,
            LastActivityAt = request.LastActivityAt,
            SoftLockState = LidGuardSessionSoftLockState.None,
            SoftLockReason = string.Empty,
            SoftLockedAt = null,
            WatchedProcessIdentifier = request.WatchedProcessIdentifier,
            WatchRegistrationKind = request.WatchRegistrationKind,
            WorkingDirectory = request.WorkingDirectory,
            TranscriptPath = request.TranscriptPath
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
        var key = new LidGuardSessionKey(request.Provider, request.SessionIdentifier, request.ProviderName);

        lock (_gate)
        {
            if (_sessions.Remove(key, out snapshot)) return true;
        }

        snapshot = LidGuardSessionSnapshot.Empty;
        return false;
    }

    public bool TryMarkActive(AgentProvider provider, string sessionIdentifier, out LidGuardSessionSnapshot snapshot, out bool changed)
        => TryMarkActive(provider, sessionIdentifier, string.Empty, out snapshot, out changed);

    public bool TryMarkActive(
        AgentProvider provider,
        string sessionIdentifier,
        string providerName,
        out LidGuardSessionSnapshot snapshot,
        out bool changed)
    {
        changed = false;
        snapshot = LidGuardSessionSnapshot.Empty;
        if (string.IsNullOrWhiteSpace(sessionIdentifier)) return false;

        var key = new LidGuardSessionKey(provider, sessionIdentifier, providerName);
        lock (_gate)
        {
            if (!_sessions.TryGetValue(key, out var existingSnapshot)) return false;

            var lastActivityAt = DateTimeOffset.UtcNow;
            if (!existingSnapshot.IsSoftLocked)
            {
                snapshot = CloneSnapshot(existingSnapshot, LidGuardSessionSoftLockState.None, string.Empty, null, lastActivityAt);
                _sessions[key] = snapshot;
                return true;
            }

            snapshot = CloneSnapshot(existingSnapshot, LidGuardSessionSoftLockState.None, string.Empty, null, lastActivityAt);
            _sessions[key] = snapshot;
            changed = true;
            return true;
        }
    }

    public bool TryMarkSoftLocked(
        AgentProvider provider,
        string sessionIdentifier,
        string providerName,
        string softLockReason,
        DateTimeOffset softLockedAt,
        out LidGuardSessionSnapshot snapshot,
        out bool changed)
    {
        changed = false;
        snapshot = LidGuardSessionSnapshot.Empty;
        if (string.IsNullOrWhiteSpace(sessionIdentifier)) return false;

        var key = new LidGuardSessionKey(provider, sessionIdentifier, providerName);
        lock (_gate)
        {
            if (!_sessions.TryGetValue(key, out var existingSnapshot)) return false;

            var normalizedSoftLockReason = softLockReason?.Trim() ?? string.Empty;
            if (existingSnapshot.IsSoftLocked && existingSnapshot.SoftLockReason.Equals(normalizedSoftLockReason, StringComparison.Ordinal))
            {
                snapshot = existingSnapshot;
                return true;
            }

            snapshot = CloneSnapshot(
                existingSnapshot,
                LidGuardSessionSoftLockState.SoftLocked,
                normalizedSoftLockReason,
                softLockedAt,
                existingSnapshot.LastActivityAt);
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
        DateTimeOffset? softLockedAt,
        DateTimeOffset lastActivityAt)
    {
        return new LidGuardSessionSnapshot
        {
            SessionIdentifier = snapshot.SessionIdentifier,
            Provider = snapshot.Provider,
            ProviderName = snapshot.ProviderName,
            StartedAt = snapshot.StartedAt,
            LastActivityAt = lastActivityAt,
            SoftLockState = softLockState,
            SoftLockReason = softLockReason,
            SoftLockedAt = softLockedAt,
            WatchedProcessIdentifier = snapshot.WatchedProcessIdentifier,
            WatchRegistrationKind = snapshot.WatchRegistrationKind,
            WorkingDirectory = snapshot.WorkingDirectory,
            TranscriptPath = snapshot.TranscriptPath
        };
    }
}
