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

    public bool HasSessionTimeoutMinutes { get; init; }

    public int? SessionTimeoutMinutes { get; init; }

    public bool? EmergencyHibernationOnHighTemperature { get; init; }

    public EmergencyHibernationTemperatureMode? EmergencyHibernationTemperatureMode { get; init; }

    public int? EmergencyHibernationTemperatureCelsius { get; init; }

    public SystemSuspendMode? SuspendMode { get; init; }

    public int? PostStopSuspendDelaySeconds { get; init; }

    public string PostStopSuspendSound { get; init; }

    public bool HasPostStopSuspendSoundVolumeOverridePercent { get; init; }

    public int? PostStopSuspendSoundVolumeOverridePercent { get; init; }

    public bool HasSuspendHistoryEntryCount { get; init; }

    public int? SuspendHistoryEntryCount { get; init; }

    public string PreSuspendWebhookUrl { get; init; }

    public ClosedLidPermissionRequestDecision? ClosedLidPermissionRequestDecision { get; init; }

    public string PowerRequestReason { get; init; }
}
