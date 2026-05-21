using LidGuard.Power;
using LidGuard.Settings;

namespace LidGuard.Ipc;

internal sealed class LidGuardPipeResponse
{
    public bool Succeeded { get; init; }

    public bool RuntimeUnavailable { get; init; }

    public string Message { get; init; } = string.Empty;

    public string MessageCode { get; init; } = string.Empty;

    public string[] MessageArguments { get; init; } = [];

    public int ActiveSessionCount { get; init; }

    public LidGuardSessionStatus[] Sessions { get; init; } = [];

    public LidGuardSettings Settings { get; init; } = LidGuardSettings.Default;

    public LidSwitchState LidSwitchState { get; init; } = LidSwitchState.Unknown;

    public int VisibleDisplayMonitorCount { get; init; }

    public bool SuspendScheduled { get; init; }

    public SystemSuspendMode SuspendMode { get; init; } = SystemSuspendMode.Sleep;

    public int SuspendDelaySeconds { get; init; }

    public string SuspendReasonCode { get; init; } = string.Empty;

    public bool StopContinuationRequested { get; init; }

    public string StopContinuationPrompt { get; init; } = string.Empty;

    public string StopFollowUpStatus { get; init; } = string.Empty;

    public static LidGuardPipeResponse Success(
        string message,
        int activeSessionCount,
        LidGuardSessionStatus[] sessions,
        LidGuardSettings settings,
        LidSwitchState lidSwitchState = LidSwitchState.Unknown,
        int visibleDisplayMonitorCount = 0,
        string messageCode = "",
        string[] messageArguments = null,
        bool suspendScheduled = false,
        SystemSuspendMode suspendMode = SystemSuspendMode.Sleep,
        int suspendDelaySeconds = 0,
        string suspendReasonCode = "",
        bool stopContinuationRequested = false,
        string stopContinuationPrompt = "",
        string stopFollowUpStatus = "") => new()
    {
        Succeeded = true,
        Message = message,
        MessageCode = messageCode ?? string.Empty,
        MessageArguments = messageArguments ?? [],
        ActiveSessionCount = activeSessionCount,
        Sessions = sessions,
        Settings = settings,
        LidSwitchState = lidSwitchState,
        VisibleDisplayMonitorCount = visibleDisplayMonitorCount,
        SuspendScheduled = suspendScheduled,
        SuspendMode = suspendMode,
        SuspendDelaySeconds = suspendDelaySeconds,
        SuspendReasonCode = suspendReasonCode ?? string.Empty,
        StopContinuationRequested = stopContinuationRequested,
        StopContinuationPrompt = stopContinuationPrompt ?? string.Empty,
        StopFollowUpStatus = stopFollowUpStatus ?? string.Empty
    };

    public static LidGuardPipeResponse Failure(
        string message,
        int activeSessionCount = 0,
        bool runtimeUnavailable = false,
        LidSwitchState lidSwitchState = LidSwitchState.Unknown,
        int visibleDisplayMonitorCount = 0,
        string messageCode = "",
        string[] messageArguments = null) => new()
    {
        Succeeded = false,
        RuntimeUnavailable = runtimeUnavailable,
        Message = message,
        MessageCode = messageCode ?? string.Empty,
        MessageArguments = messageArguments ?? [],
        ActiveSessionCount = activeSessionCount,
        LidSwitchState = lidSwitchState,
        VisibleDisplayMonitorCount = visibleDisplayMonitorCount
    };
}

