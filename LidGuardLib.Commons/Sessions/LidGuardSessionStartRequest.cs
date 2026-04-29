namespace LidGuardLib.Commons.Sessions;

public sealed class LidGuardSessionStartRequest
{
    public required string SessionIdentifier { get; init; }

    public AgentProvider Provider { get; init; } = AgentProvider.Unknown;

    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;

    public int WatchedProcessIdentifier { get; init; }

    public string WorkingDirectory { get; init; } = string.Empty;
}
