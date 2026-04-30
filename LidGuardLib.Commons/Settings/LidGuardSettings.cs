using LidGuardLib.Commons.Power;

namespace LidGuardLib.Commons.Settings;

public sealed class LidGuardSettings
{
    public const int MinimumEmergencyHibernationTemperatureCelsius = 70;
    public const int MaximumEmergencyHibernationTemperatureCelsius = 110;
    public const int DefaultEmergencyHibernationTemperatureCelsius = 93;

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

    public string PreSuspendWebhookUrl { get; init; } = string.Empty;

    public ClosedLidPermissionRequestDecision ClosedLidPermissionRequestDecision { get; init; } = ClosedLidPermissionRequestDecision.Deny;

    public bool WatchParentProcess { get; init; } = true;

    public bool EmergencyHibernationOnHighTemperature { get; init; } = true;

    public int EmergencyHibernationTemperatureCelsius { get; init; } = DefaultEmergencyHibernationTemperatureCelsius;

    public static int ClampEmergencyHibernationTemperatureCelsius(int emergencyHibernationTemperatureCelsius)
        => Math.Clamp(
            emergencyHibernationTemperatureCelsius,
            MinimumEmergencyHibernationTemperatureCelsius,
            MaximumEmergencyHibernationTemperatureCelsius);

    public static LidGuardSettings Normalize(LidGuardSettings settings)
    {
        if (settings is null) return HeadlessRuntimeDefault;

        var powerRequest = settings.PowerRequest ?? PowerRequestOptions.Default;
        var emergencyHibernationTemperatureCelsius = ClampEmergencyHibernationTemperatureCelsius(settings.EmergencyHibernationTemperatureCelsius);
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
            PreSuspendWebhookUrl = string.IsNullOrWhiteSpace(settings.PreSuspendWebhookUrl) ? string.Empty : settings.PreSuspendWebhookUrl.Trim(),
            ClosedLidPermissionRequestDecision = settings.ClosedLidPermissionRequestDecision,
            WatchParentProcess = settings.WatchParentProcess,
            EmergencyHibernationOnHighTemperature = settings.EmergencyHibernationOnHighTemperature,
            EmergencyHibernationTemperatureCelsius = emergencyHibernationTemperatureCelsius
        };
    }
}
