using LidGuardLib.Commons.Power;

namespace LidGuardLib.Commons.Settings;

public sealed class LidGuardSettings
{
    public const int MinimumEmergencyHibernationTemperatureCelsius = 70;
    public const int MaximumEmergencyHibernationTemperatureCelsius = 110;
    public const int DefaultEmergencyHibernationTemperatureCelsius = 93;
    public const int MinimumPostStopSuspendSoundVolumeOverridePercent = 1;
    public const int MaximumPostStopSuspendSoundVolumeOverridePercent = 100;
    public const int MinimumSuspendHistoryEntryCount = 1;
    public const int DefaultSuspendHistoryEntryCount = 10;

    public static LidGuardSettings Default { get; } = new();

    public static LidGuardSettings HeadlessRuntimeDefault { get; } = new()
    {
        ChangeLidAction = true,
        WatchParentProcess = true
    };

    public PowerRequestOptions PowerRequest { get; init; } = PowerRequestOptions.Default;

    public bool ChangeLidAction { get; init; }

    public SystemSuspendMode SuspendMode { get; init; } = SystemSuspendMode.Sleep;

    public int PostStopSuspendDelaySeconds { get; init; } = 10;

    public string PostStopSuspendSound { get; init; } = string.Empty;

    public int? PostStopSuspendSoundVolumeOverridePercent { get; init; }

    public int? SuspendHistoryEntryCount { get; init; } = DefaultSuspendHistoryEntryCount;

    public string PreSuspendWebhookUrl { get; init; } = string.Empty;

    public ClosedLidPermissionRequestDecision ClosedLidPermissionRequestDecision { get; init; } = ClosedLidPermissionRequestDecision.Deny;

    public bool WatchParentProcess { get; init; } = true;

    public bool EmergencyHibernationOnHighTemperature { get; init; } = true;

    public EmergencyHibernationTemperatureMode EmergencyHibernationTemperatureMode { get; init; } = EmergencyHibernationTemperatureMode.Average;

    public int EmergencyHibernationTemperatureCelsius { get; init; } = DefaultEmergencyHibernationTemperatureCelsius;

    public static int ClampEmergencyHibernationTemperatureCelsius(int emergencyHibernationTemperatureCelsius)
        => Math.Clamp(
            emergencyHibernationTemperatureCelsius,
            MinimumEmergencyHibernationTemperatureCelsius,
            MaximumEmergencyHibernationTemperatureCelsius);

    public static bool IsValidPostStopSuspendSoundVolumeOverridePercent(int? postStopSuspendSoundVolumeOverridePercent)
        => postStopSuspendSoundVolumeOverridePercent is null
            || postStopSuspendSoundVolumeOverridePercent is >= MinimumPostStopSuspendSoundVolumeOverridePercent and <= MaximumPostStopSuspendSoundVolumeOverridePercent;

    public static bool IsValidSuspendHistoryEntryCount(int? suspendHistoryEntryCount)
        => suspendHistoryEntryCount is null || suspendHistoryEntryCount >= MinimumSuspendHistoryEntryCount;

    public static LidGuardSettings Normalize(LidGuardSettings settings)
    {
        if (settings is null) return HeadlessRuntimeDefault;

        var powerRequest = settings.PowerRequest ?? PowerRequestOptions.Default;
        var emergencyHibernationTemperatureMode = NormalizeEmergencyHibernationTemperatureMode(settings.EmergencyHibernationTemperatureMode);
        var emergencyHibernationTemperatureCelsius = ClampEmergencyHibernationTemperatureCelsius(settings.EmergencyHibernationTemperatureCelsius);
        var suspendHistoryEntryCount = settings.SuspendHistoryEntryCount is null
            ? null
            : Math.Max(MinimumSuspendHistoryEntryCount, settings.SuspendHistoryEntryCount.Value);
        return new LidGuardSettings
        {
            PowerRequest = new PowerRequestOptions
            {
                PreventSystemSleep = powerRequest.PreventSystemSleep,
                PreventAwayModeSleep = powerRequest.PreventAwayModeSleep,
                PreventDisplaySleep = powerRequest.PreventDisplaySleep,
                Reason = string.IsNullOrWhiteSpace(powerRequest.Reason) ? PowerRequestOptions.Default.Reason : powerRequest.Reason
            },
            ChangeLidAction = settings.ChangeLidAction,
            SuspendMode = settings.SuspendMode,
            PostStopSuspendDelaySeconds = Math.Max(0, settings.PostStopSuspendDelaySeconds),
            PostStopSuspendSound = string.IsNullOrWhiteSpace(settings.PostStopSuspendSound) ? string.Empty : settings.PostStopSuspendSound.Trim(),
            PostStopSuspendSoundVolumeOverridePercent = settings.PostStopSuspendSoundVolumeOverridePercent,
            SuspendHistoryEntryCount = suspendHistoryEntryCount,
            PreSuspendWebhookUrl = string.IsNullOrWhiteSpace(settings.PreSuspendWebhookUrl) ? string.Empty : settings.PreSuspendWebhookUrl.Trim(),
            ClosedLidPermissionRequestDecision = settings.ClosedLidPermissionRequestDecision,
            WatchParentProcess = settings.WatchParentProcess,
            EmergencyHibernationOnHighTemperature = settings.EmergencyHibernationOnHighTemperature,
            EmergencyHibernationTemperatureMode = emergencyHibernationTemperatureMode,
            EmergencyHibernationTemperatureCelsius = emergencyHibernationTemperatureCelsius
        };
    }

    private static EmergencyHibernationTemperatureMode NormalizeEmergencyHibernationTemperatureMode(EmergencyHibernationTemperatureMode emergencyHibernationTemperatureMode)
        => emergencyHibernationTemperatureMode switch
        {
            EmergencyHibernationTemperatureMode.Low => EmergencyHibernationTemperatureMode.Low,
            EmergencyHibernationTemperatureMode.High => EmergencyHibernationTemperatureMode.High,
            _ => EmergencyHibernationTemperatureMode.Average
        };
}
