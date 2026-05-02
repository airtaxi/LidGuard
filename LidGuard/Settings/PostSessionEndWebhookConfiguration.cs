using LidGuard.Settings;

namespace LidGuard.Settings;

internal static class PostSessionEndWebhookConfiguration
{
    public static string GetDisplayValue(string postSessionEndWebhookUrl)
        => WebhookUrlConfiguration.GetDisplayValue(postSessionEndWebhookUrl);

    public static LidGuardSettings WithPostSessionEndWebhookUrl(LidGuardSettings settings, string postSessionEndWebhookUrl)
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
            PreSuspendWebhookUrl = normalizedInputSettings.PreSuspendWebhookUrl,
            PostSessionEndWebhookUrl = postSessionEndWebhookUrl,
            ClosedLidPermissionRequestDecision = normalizedInputSettings.ClosedLidPermissionRequestDecision,
            WatchParentProcess = normalizedInputSettings.WatchParentProcess,
            SessionTimeoutMinutes = normalizedInputSettings.SessionTimeoutMinutes,
            ServerRuntimeCleanupDelayMinutes = normalizedInputSettings.ServerRuntimeCleanupDelayMinutes,
            EmergencyHibernationOnHighTemperature = normalizedInputSettings.EmergencyHibernationOnHighTemperature,
            EmergencyHibernationTemperatureMode = normalizedInputSettings.EmergencyHibernationTemperatureMode,
            EmergencyHibernationTemperatureCelsius = normalizedInputSettings.EmergencyHibernationTemperatureCelsius
        };
    }

    public static bool TryNormalizeConfiguredValue(string postSessionEndWebhookUrl, out string normalizedPostSessionEndWebhookUrl, out string message)
        => WebhookUrlConfiguration.TryNormalizeConfiguredValue(
            postSessionEndWebhookUrl,
            "post-session-end",
            out normalizedPostSessionEndWebhookUrl,
            out message);
}
