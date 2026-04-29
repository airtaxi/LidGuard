using System.Text.Json.Serialization;
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

    public bool SuspendWhenStoppedAndLidClosed { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<SystemSuspendMode>))]
    public SystemSuspendMode SuspendMode { get; init; } = SystemSuspendMode.Sleep;

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
            SuspendWhenStoppedAndLidClosed = settings.SuspendWhenStoppedAndLidClosed,
            SuspendMode = settings.SuspendMode,
            WatchParentProcess = settings.WatchParentProcess
        };
    }
}
