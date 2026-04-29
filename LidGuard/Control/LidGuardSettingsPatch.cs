using LidGuardLib.Commons.Power;
using LidGuardLib.Commons.Settings;

namespace LidGuard.Control;

public sealed class LidGuardSettingsPatch
{
    public bool ResetToDefaults { get; init; }

    public bool? PreventSystemSleep { get; init; }

    public bool? PreventAwayModeSleep { get; init; }

    public bool? PreventDisplaySleep { get; init; }

    public bool? ChangeLidAction { get; init; }

    public bool? WatchParentProcess { get; init; }

    public SystemSuspendMode? SuspendMode { get; init; }

    public int? PostStopSuspendDelaySeconds { get; init; }

    public string PostStopSuspendSound { get; init; }

    public ClosedLidPermissionRequestDecision? ClosedLidPermissionRequestDecision { get; init; }

    public string PowerRequestReason { get; init; }
}
