namespace LidGuard.Settings;

internal static class ClosedLidStopFollowUpWebhookConfiguration
{
    public static string GetDisplayValue(string closedLidStopFollowUpWebhookUrl)
        => ClosedLidStopFollowUpConfiguration.GetDisplayValue(closedLidStopFollowUpWebhookUrl);

    public static LidGuardSettings WithClosedLidStopFollowUpWebhookUrl(LidGuardSettings settings, string closedLidStopFollowUpWebhookUrl)
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
            PostSessionEndWebhookUrl = normalizedInputSettings.PostSessionEndWebhookUrl,
            ClosedLidStopFollowUpWebhookUrl = closedLidStopFollowUpWebhookUrl,
            ClosedLidStopFollowUpDelaySeconds = normalizedInputSettings.ClosedLidStopFollowUpDelaySeconds,
            RepeatClosedLidStopFollowUp = normalizedInputSettings.RepeatClosedLidStopFollowUp,
            ClosedLidPermissionRequestDecision = normalizedInputSettings.ClosedLidPermissionRequestDecision,
            WatchParentProcess = normalizedInputSettings.WatchParentProcess,
            SessionTimeoutMinutes = normalizedInputSettings.SessionTimeoutMinutes,
            ServerRuntimeCleanupDelayMinutes = normalizedInputSettings.ServerRuntimeCleanupDelayMinutes,
            EmergencyHibernationOnHighTemperature = normalizedInputSettings.EmergencyHibernationOnHighTemperature,
            EmergencyHibernationTemperatureMode = normalizedInputSettings.EmergencyHibernationTemperatureMode,
            EmergencyHibernationTemperatureCelsius = normalizedInputSettings.EmergencyHibernationTemperatureCelsius,
            UserInterfaceCulture = normalizedInputSettings.UserInterfaceCulture
        };
    }

    public static bool TryNormalizeConfiguredValue(
        string closedLidStopFollowUpWebhookUrl,
        out string normalizedClosedLidStopFollowUpWebhookUrl,
        out string message)
        => ClosedLidStopFollowUpConfiguration.TryNormalizeConfiguredValue(
            closedLidStopFollowUpWebhookUrl,
            out normalizedClosedLidStopFollowUpWebhookUrl,
            out message);
}
