namespace LidGuardLib.Commons.Sessions;

public sealed class LidGuardSessionSnapshot
{
    public static LidGuardSessionSnapshot Empty { get; } = new()
    {
        SessionIdentifier = string.Empty,
        Provider = AgentProvider.Unknown,
        StartedAt = DateTimeOffset.MinValue,
        WatchedProcessIdentifier = 0,
        WorkingDirectory = string.Empty
    };

    public required string SessionIdentifier { get; init; }

    public AgentProvider Provider { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public int WatchedProcessIdentifier { get; init; }

    public string WorkingDirectory { get; init; } = string.Empty;

    public LidGuardSessionKey Key => new(Provider, SessionIdentifier);

    public bool HasWatchedProcess => WatchedProcessIdentifier > 0;
}
