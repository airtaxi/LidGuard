using LidGuardLib.Commons.Power;
using LidGuardLib.Commons.Services;
using LidGuardLib.Commons.Settings;

namespace LidGuard.Settings;

internal static class PostStopSuspendSoundConfiguration
{
    public static string GetDisplayValue(string postStopSuspendSound)
        => string.IsNullOrWhiteSpace(postStopSuspendSound) ? "off" : postStopSuspendSound;

    public static bool TryNormalize(
        LidGuardSettings settings,
        IPostStopSuspendSoundPlayer postStopSuspendSoundPlayer,
        out LidGuardSettings normalizedSettings,
        out string message)
    {
        var normalizedInputSettings = LidGuardSettings.Normalize(settings);
        var normalizeResult = postStopSuspendSoundPlayer.NormalizeConfiguration(normalizedInputSettings.PostStopSuspendSound);
        if (!normalizeResult.Succeeded)
        {
            normalizedSettings = normalizedInputSettings;
            message = normalizeResult.Message;
            return false;
        }

        var powerRequest = normalizedInputSettings.PowerRequest ?? PowerRequestOptions.Default;
        normalizedSettings = new LidGuardSettings
        {
            PowerRequest = new PowerRequestOptions
            {
                PreventSystemSleep = powerRequest.PreventSystemSleep,
                PreventAwayModeSleep = powerRequest.PreventAwayModeSleep,
                PreventDisplaySleep = powerRequest.PreventDisplaySleep,
                Reason = powerRequest.Reason
            },
            ChangeLidAction = normalizedInputSettings.ChangeLidAction,
            SuspendMode = normalizedInputSettings.SuspendMode,
            PostStopSuspendDelaySeconds = normalizedInputSettings.PostStopSuspendDelaySeconds,
            PostStopSuspendSound = normalizeResult.Value,
            PreSuspendWebhookUrl = normalizedInputSettings.PreSuspendWebhookUrl,
            ClosedLidPermissionRequestDecision = normalizedInputSettings.ClosedLidPermissionRequestDecision,
            WatchParentProcess = normalizedInputSettings.WatchParentProcess,
            EmergencyHibernationOnHighTemperature = normalizedInputSettings.EmergencyHibernationOnHighTemperature,
            EmergencyHibernationTemperatureMode = normalizedInputSettings.EmergencyHibernationTemperatureMode,
            EmergencyHibernationTemperatureCelsius = normalizedInputSettings.EmergencyHibernationTemperatureCelsius
        };

        message = string.Empty;
        return true;
    }
}
