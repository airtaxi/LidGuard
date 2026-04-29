using LidGuardLib.Commons.Power;

namespace LidGuardLib.Commons.Settings;

public sealed class LidGuardSettings
{
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

    public ClosedLidPermissionRequestDecision ClosedLidPermissionRequestDecision { get; init; } = ClosedLidPermissionRequestDecision.Deny;

    public bool WatchParentProcess { get; init; } = true;

    public static LidGuardSettings Normalize(LidGuardSettings settings)
    {
        if (settings is null) return HeadlessRuntimeDefault;

        var powerRequest = settings.PowerRequest ?? PowerRequestOptions.Default;
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
            ClosedLidPermissionRequestDecision = settings.ClosedLidPermissionRequestDecision,
            WatchParentProcess = settings.WatchParentProcess
        };
    }
}
