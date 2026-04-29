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

    public IReadOnlyList<LidGuardSessionSnapshot> GetSnapshots()
    {
        lock (_gate) return [.. _sessions.Values];
    }

    public void Clear()
    {
        lock (_gate) _sessions.Clear();
    }
}
