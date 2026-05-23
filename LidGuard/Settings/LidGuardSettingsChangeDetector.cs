using LidGuard.Power;

namespace LidGuard.Settings;

internal static class LidGuardSettingsChangeDetector
{
    public static bool AreEquivalent(LidGuardSettings firstSettings, LidGuardSettings secondSettings)
    {
        if (ReferenceEquals(firstSettings, secondSettings)) return true;
        if (firstSettings is null || secondSettings is null) return false;

        var normalizedFirstSettings = LidGuardSettings.Normalize(firstSettings);
        var normalizedSecondSettings = LidGuardSettings.Normalize(secondSettings);

        return ArePowerRequestOptionsEquivalent(normalizedFirstSettings.PowerRequest, normalizedSecondSettings.PowerRequest) && normalizedFirstSettings.ChangeLidAction == normalizedSecondSettings.ChangeLidAction && normalizedFirstSettings.SuspendMode == normalizedSecondSettings.SuspendMode && normalizedFirstSettings.PostStopSuspendDelaySeconds == normalizedSecondSettings.PostStopSuspendDelaySeconds && string.Equals(normalizedFirstSettings.PostStopSuspendSound, normalizedSecondSettings.PostStopSuspendSound, StringComparison.Ordinal) && normalizedFirstSettings.PostStopSuspendSoundVolumeOverridePercent == normalizedSecondSettings.PostStopSuspendSoundVolumeOverridePercent && string.Equals(normalizedFirstSettings.ClosedLidStopFollowUpSound, normalizedSecondSettings.ClosedLidStopFollowUpSound, StringComparison.Ordinal) && normalizedFirstSettings.ClosedLidStopFollowUpSoundVolumeOverridePercent == normalizedSecondSettings.ClosedLidStopFollowUpSoundVolumeOverridePercent && normalizedFirstSettings.SuspendHistoryEntryCount == normalizedSecondSettings.SuspendHistoryEntryCount && string.Equals(normalizedFirstSettings.PreSuspendWebhookUrl, normalizedSecondSettings.PreSuspendWebhookUrl, StringComparison.Ordinal) && string.Equals(normalizedFirstSettings.PostSessionEndWebhookUrl, normalizedSecondSettings.PostSessionEndWebhookUrl, StringComparison.Ordinal) && string.Equals(normalizedFirstSettings.ClosedLidStopFollowUpWebhookUrl, normalizedSecondSettings.ClosedLidStopFollowUpWebhookUrl, StringComparison.Ordinal) && normalizedFirstSettings.ClosedLidStopFollowUpDelaySeconds == normalizedSecondSettings.ClosedLidStopFollowUpDelaySeconds && normalizedFirstSettings.RepeatClosedLidStopFollowUp == normalizedSecondSettings.RepeatClosedLidStopFollowUp && normalizedFirstSettings.ClosedLidPermissionRequestDecision == normalizedSecondSettings.ClosedLidPermissionRequestDecision && normalizedFirstSettings.WatchParentProcess == normalizedSecondSettings.WatchParentProcess && normalizedFirstSettings.SessionTimeoutMinutes == normalizedSecondSettings.SessionTimeoutMinutes && normalizedFirstSettings.ServerRuntimeCleanupDelayMinutes == normalizedSecondSettings.ServerRuntimeCleanupDelayMinutes && normalizedFirstSettings.EmergencyHibernationOnHighTemperature == normalizedSecondSettings.EmergencyHibernationOnHighTemperature && normalizedFirstSettings.EmergencyHibernationTemperatureMode == normalizedSecondSettings.EmergencyHibernationTemperatureMode && normalizedFirstSettings.EmergencyHibernationTemperatureCelsius == normalizedSecondSettings.EmergencyHibernationTemperatureCelsius && string.Equals(normalizedFirstSettings.UserInterfaceCulture, normalizedSecondSettings.UserInterfaceCulture, StringComparison.Ordinal);
    }

    public static string[] DescribeChanges(LidGuardSettings previousSettings, LidGuardSettings updatedSettings)
    {
        var normalizedPreviousSettings = LidGuardSettings.Normalize(previousSettings);
        var normalizedUpdatedSettings = LidGuardSettings.Normalize(updatedSettings);
        var previousPowerRequest = normalizedPreviousSettings.PowerRequest ?? PowerRequestOptions.Default;
        var updatedPowerRequest = normalizedUpdatedSettings.PowerRequest ?? PowerRequestOptions.Default;
        var changes = new List<string>();

        AppendChange(changes, previousPowerRequest.PreventSystemSleep, updatedPowerRequest.PreventSystemSleep, "preventSystemSleep");
#if !LIDGUARD_LINUX && !LIDGUARD_MACOS
        AppendChange(changes, previousPowerRequest.PreventAwayModeSleep, updatedPowerRequest.PreventAwayModeSleep, "preventAwayModeSleep");
#endif
        AppendChange(changes, previousPowerRequest.PreventDisplaySleep, updatedPowerRequest.PreventDisplaySleep, "preventDisplaySleep");
        AppendChange(changes, previousPowerRequest.Reason, updatedPowerRequest.Reason, "powerRequestReason");
        AppendChange(changes, normalizedPreviousSettings.ChangeLidAction, normalizedUpdatedSettings.ChangeLidAction, "changeLidAction");
        AppendChange(changes, normalizedPreviousSettings.WatchParentProcess, normalizedUpdatedSettings.WatchParentProcess, "watchParentProcess");
        AppendChange(changes, normalizedPreviousSettings.SuspendMode, normalizedUpdatedSettings.SuspendMode, "suspendMode");
        AppendChange(changes, normalizedPreviousSettings.PostStopSuspendDelaySeconds, normalizedUpdatedSettings.PostStopSuspendDelaySeconds, "postStopSuspendDelaySeconds");
        AppendChange(changes, normalizedPreviousSettings.PostStopSuspendSound, normalizedUpdatedSettings.PostStopSuspendSound, "postStopSuspendSound");
        AppendChange(changes, normalizedPreviousSettings.PostStopSuspendSoundVolumeOverridePercent, normalizedUpdatedSettings.PostStopSuspendSoundVolumeOverridePercent, "postStopSuspendSoundVolumeOverridePercent");
        AppendChange(changes, normalizedPreviousSettings.ClosedLidStopFollowUpSound, normalizedUpdatedSettings.ClosedLidStopFollowUpSound, "closedLidStopFollowUpSound");
        AppendChange(changes, normalizedPreviousSettings.ClosedLidStopFollowUpSoundVolumeOverridePercent, normalizedUpdatedSettings.ClosedLidStopFollowUpSoundVolumeOverridePercent, "closedLidStopFollowUpSoundVolumeOverridePercent");
        AppendChange(changes, normalizedPreviousSettings.SuspendHistoryEntryCount, normalizedUpdatedSettings.SuspendHistoryEntryCount, "suspendHistoryEntryCount");
        AppendChange(changes, normalizedPreviousSettings.PreSuspendWebhookUrl, normalizedUpdatedSettings.PreSuspendWebhookUrl, "preSuspendWebhookUrl");
        AppendChange(changes, normalizedPreviousSettings.PostSessionEndWebhookUrl, normalizedUpdatedSettings.PostSessionEndWebhookUrl, "postSessionEndWebhookUrl");
        AppendChange(changes, normalizedPreviousSettings.ClosedLidStopFollowUpWebhookUrl, normalizedUpdatedSettings.ClosedLidStopFollowUpWebhookUrl, "closedLidStopFollowUpWebhookUrl");
        AppendChange(changes, normalizedPreviousSettings.ClosedLidStopFollowUpDelaySeconds, normalizedUpdatedSettings.ClosedLidStopFollowUpDelaySeconds, "closedLidStopFollowUpDelaySeconds");
        AppendChange(changes, normalizedPreviousSettings.RepeatClosedLidStopFollowUp, normalizedUpdatedSettings.RepeatClosedLidStopFollowUp, "repeatClosedLidStopFollowUp");
        AppendChange(changes, normalizedPreviousSettings.ClosedLidPermissionRequestDecision, normalizedUpdatedSettings.ClosedLidPermissionRequestDecision, "closedLidPermissionRequestDecision");
        AppendChange(changes, normalizedPreviousSettings.SessionTimeoutMinutes, normalizedUpdatedSettings.SessionTimeoutMinutes, "sessionTimeoutMinutes");
        AppendChange(changes, normalizedPreviousSettings.ServerRuntimeCleanupDelayMinutes, normalizedUpdatedSettings.ServerRuntimeCleanupDelayMinutes, "serverRuntimeCleanupDelayMinutes");
        AppendChange(changes, normalizedPreviousSettings.EmergencyHibernationOnHighTemperature, normalizedUpdatedSettings.EmergencyHibernationOnHighTemperature, "emergencyHibernationOnHighTemperature");
        AppendChange(changes, normalizedPreviousSettings.EmergencyHibernationTemperatureMode, normalizedUpdatedSettings.EmergencyHibernationTemperatureMode, "emergencyHibernationTemperatureMode");
        AppendChange(changes, normalizedPreviousSettings.EmergencyHibernationTemperatureCelsius, normalizedUpdatedSettings.EmergencyHibernationTemperatureCelsius, "emergencyHibernationTemperatureCelsius");
        AppendChange(changes, normalizedPreviousSettings.UserInterfaceCulture, normalizedUpdatedSettings.UserInterfaceCulture, "userInterfaceCulture");

        return [.. changes];
    }

    public static bool RequiresManagedHookRefresh(LidGuardSettings previousSettings, LidGuardSettings updatedSettings)
    {
        var normalizedPreviousSettings = LidGuardSettings.Normalize(previousSettings);
        var normalizedUpdatedSettings = LidGuardSettings.Normalize(updatedSettings);
        if (!normalizedPreviousSettings.UserInterfaceCulture.Equals(normalizedUpdatedSettings.UserInterfaceCulture, StringComparison.OrdinalIgnoreCase)) return true;
        return ClosedLidStopFollowUpConfiguration.GetManagedHookTimeoutSeconds(normalizedPreviousSettings)
            != ClosedLidStopFollowUpConfiguration.GetManagedHookTimeoutSeconds(normalizedUpdatedSettings);
    }

    private static bool ArePowerRequestOptionsEquivalent(PowerRequestOptions firstPowerRequestOptions, PowerRequestOptions secondPowerRequestOptions)
    {
        if (ReferenceEquals(firstPowerRequestOptions, secondPowerRequestOptions)) return true;
        if (firstPowerRequestOptions is null || secondPowerRequestOptions is null) return false;

        return firstPowerRequestOptions.PreventSystemSleep == secondPowerRequestOptions.PreventSystemSleep && firstPowerRequestOptions.PreventAwayModeSleep == secondPowerRequestOptions.PreventAwayModeSleep && firstPowerRequestOptions.PreventDisplaySleep == secondPowerRequestOptions.PreventDisplaySleep && string.Equals(firstPowerRequestOptions.Reason, secondPowerRequestOptions.Reason, StringComparison.Ordinal);
    }

    private static void AppendChange<TValue>(List<string> changes, TValue previousValue, TValue updatedValue, string changeName)
    {
        if (EqualityComparer<TValue>.Default.Equals(previousValue, updatedValue)) return;
        changes.Add(changeName);
    }
}
