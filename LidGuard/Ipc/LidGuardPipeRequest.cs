using LidGuardLib.Commons.Sessions;
using LidGuardLib.Commons.Settings;

namespace LidGuard.Ipc;

internal sealed class LidGuardPipeRequest
{
    public string Command { get; init; } = string.Empty;

    public AgentProvider Provider { get; init; } = AgentProvider.Unknown;

    public string SessionIdentifier { get; init; } = string.Empty;

    public bool MatchAllProvidersForSessionIdentifier { get; init; }

    public int WatchedProcessIdentifier { get; init; }

    public string SessionStateReason { get; init; } = string.Empty;

    public string WorkingDirectory { get; init; } = string.Empty;

    public bool HasSettings { get; init; }

    public LidGuardSettings Settings { get; init; } = LidGuardSettings.Default;
}

