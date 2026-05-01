using LidGuardLib.Commons.Settings;

namespace LidGuard.Settings;

internal static class PreSuspendWebhookConfiguration
{
    public static string GetDisplayValue(string preSuspendWebhookUrl)
        => string.IsNullOrWhiteSpace(preSuspendWebhookUrl) ? "off" : preSuspendWebhookUrl;

    public static LidGuardSettings WithPreSuspendWebhookUrl(LidGuardSettings settings, string preSuspendWebhookUrl)
    {
        var normalizedInputSettings = LidGuardSettings.Normalize(settings);
        return new LidGuardSettings
        {
            PowerRequest = normalizedInputSettings.PowerRequest,
            ChangeLidAction = normalizedInputSettings.ChangeLidAction,
            SuspendMode = normalizedInputSettings.SuspendMode,
            PostStopSuspendDelaySeconds = normalizedInputSettings.PostStopSuspendDelaySeconds,
            PostStopSuspendSound = normalizedInputSettings.PostStopSuspendSound,
            PostStopSuspendSoundVolumeOverridePercent = normalizedInputSettings.PostStopSuspendSoundVolumeOverridePercent,
            SuspendHistoryEntryCount = normalizedInputSettings.SuspendHistoryEntryCount,
            PreSuspendWebhookUrl = preSuspendWebhookUrl,
            ClosedLidPermissionRequestDecision = normalizedInputSettings.ClosedLidPermissionRequestDecision,
            WatchParentProcess = normalizedInputSettings.WatchParentProcess,
            EmergencyHibernationOnHighTemperature = normalizedInputSettings.EmergencyHibernationOnHighTemperature,
            EmergencyHibernationTemperatureMode = normalizedInputSettings.EmergencyHibernationTemperatureMode,
            EmergencyHibernationTemperatureCelsius = normalizedInputSettings.EmergencyHibernationTemperatureCelsius
        };
    }

    public static bool TryNormalizeConfiguredValue(string preSuspendWebhookUrl, out string normalizedPreSuspendWebhookUrl, out string message)
    {
        normalizedPreSuspendWebhookUrl = string.Empty;
        message = string.Empty;

        if (string.IsNullOrWhiteSpace(preSuspendWebhookUrl)) return true;

        var normalizedInput = preSuspendWebhookUrl.Trim();
        if (!Uri.TryCreate(normalizedInput, UriKind.Absolute, out var webhookUri) ||
            (webhookUri.Scheme != Uri.UriSchemeHttp && webhookUri.Scheme != Uri.UriSchemeHttps))
        {
            message = "The pre-suspend webhook URL must be empty or an absolute HTTP or HTTPS URL.";
            return false;
        }

        normalizedPreSuspendWebhookUrl = webhookUri.AbsoluteUri;
        return true;
    }
}
