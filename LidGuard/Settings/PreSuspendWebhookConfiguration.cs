using LidGuard.Settings;

namespace LidGuard.Settings;

internal static class PreSuspendWebhookConfiguration
{
    public static string GetDisplayValue(string preSuspendWebhookUrl)
        => WebhookUrlConfiguration.GetDisplayValue(preSuspendWebhookUrl);

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
            PostSessionEndWebhookUrl = normalizedInputSettings.PostSessionEndWebhookUrl,
            ClosedLidPermissionRequestDecision = normalizedInputSettings.ClosedLidPermissionRequestDecision,
            WatchParentProcess = normalizedInputSettings.WatchParentProcess,
            SessionTimeoutMinutes = normalizedInputSettings.SessionTimeoutMinutes,
            ServerRuntimeCleanupDelayMinutes = normalizedInputSettings.ServerRuntimeCleanupDelayMinutes,
            EmergencyHibernationOnHighTemperature = normalizedInputSettings.EmergencyHibernationOnHighTemperature,
            EmergencyHibernationTemperatureMode = normalizedInputSettings.EmergencyHibernationTemperatureMode,
            EmergencyHibernationTemperatureCelsius = normalizedInputSettings.EmergencyHibernationTemperatureCelsius
        };
    }

    public static bool TryNormalizeConfiguredValue(string preSuspendWebhookUrl, out string normalizedPreSuspendWebhookUrl, out string message)
        => WebhookUrlConfiguration.TryNormalizeConfiguredValue(
            preSuspendWebhookUrl,
            "pre-suspend",
            out normalizedPreSuspendWebhookUrl,
            out message);
}
