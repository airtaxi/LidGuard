using LidGuard.Power;
using LidGuard.Settings;

namespace LidGuard.Ipc;

internal sealed class LidGuardPipeResponse
{
    public bool Succeeded { get; init; }

    public bool RuntimeUnavailable { get; init; }

    public string Message { get; init; } = string.Empty;

    public int ActiveSessionCount { get; init; }

    public LidGuardSessionStatus[] Sessions { get; init; } = [];

    public LidGuardSettings Settings { get; init; } = LidGuardSettings.Default;

    public LidSwitchState LidSwitchState { get; init; } = LidSwitchState.Unknown;

    public int VisibleDisplayMonitorCount { get; init; }

    public static LidGuardPipeResponse Success(
        string message,
        int activeSessionCount,
        LidGuardSessionStatus[] sessions,
        LidGuardSettings settings,
        LidSwitchState lidSwitchState = LidSwitchState.Unknown,
        int visibleDisplayMonitorCount = 0) => new()
    {
        Succeeded = true,
        Message = message,
        ActiveSessionCount = activeSessionCount,
        Sessions = sessions,
        Settings = settings,
        LidSwitchState = lidSwitchState,
        VisibleDisplayMonitorCount = visibleDisplayMonitorCount
    };

    public static LidGuardPipeResponse Failure(
        string message,
        int activeSessionCount = 0,
        bool runtimeUnavailable = false,
        LidSwitchState lidSwitchState = LidSwitchState.Unknown,
        int visibleDisplayMonitorCount = 0) => new()
    {
        Succeeded = false,
        RuntimeUnavailable = runtimeUnavailable,
        Message = message,
        ActiveSessionCount = activeSessionCount,
        LidSwitchState = lidSwitchState,
        VisibleDisplayMonitorCount = visibleDisplayMonitorCount
    };
}

