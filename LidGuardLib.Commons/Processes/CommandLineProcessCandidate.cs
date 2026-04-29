using LidGuardLib.Commons.Sessions;

namespace LidGuardLib.Commons.Processes;

public sealed class CommandLineProcessCandidate
{
    public required int ProcessIdentifier { get; init; }

    public required string ProcessName { get; init; }

    public required string WorkingDirectory { get; init; }

    public AgentProvider Provider { get; init; } = AgentProvider.Unknown;

    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.MinValue;
}
