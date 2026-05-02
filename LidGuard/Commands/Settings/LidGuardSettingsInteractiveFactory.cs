using LidGuard.Power;
using LidGuard.Settings;

namespace LidGuard.Commands;

internal static class LidGuardSettingsInteractiveFactory
{
    public static bool TryCreateSettings(LidGuardSettings currentSettings, out LidGuardSettings settings, out string message)
    {
        var normalizedStoredSettings = LidGuardSettings.Normalize(currentSettings);
        var storedPowerRequest = normalizedStoredSettings.PowerRequest ?? PowerRequestOptions.Default;
        var defaultSettings = LidGuardSettings.Normalize(LidGuardSettings.HeadlessRuntimeDefault);
        var defaultPowerRequest = defaultSettings.PowerRequest ?? PowerRequestOptions.Default;
        settings = normalizedStoredSettings;
        message = string.Empty;

        if (!LidGuardSettingsInteractivePromptReader.TryReadBooleanSetting("Prevent system sleep", storedPowerRequest.PreventSystemSleep, defaultPowerRequest.PreventSystemSleep, out var preventSystemSleep, out message)) return false;
        if (!LidGuardSettingsInteractivePromptReader.TryReadBooleanSetting("Prevent away mode sleep", storedPowerRequest.PreventAwayModeSleep, defaultPowerRequest.PreventAwayModeSleep, out var preventAwayModeSleep, out message)) return false;
        if (!LidGuardSettingsInteractivePromptReader.TryReadBooleanSetting("Prevent display sleep", storedPowerRequest.PreventDisplaySleep, defaultPowerRequest.PreventDisplaySleep, out var preventDisplaySleep, out message)) return false;
        if (!LidGuardSettingsInteractivePromptReader.TryReadBooleanSetting("Change lid action", normalizedStoredSettings.ChangeLidAction, defaultSettings.ChangeLidAction, out var changeLidAction, out message)) return false;
        if (!LidGuardSettingsInteractivePromptReader.TryReadBooleanSetting("Watch parent process", normalizedStoredSettings.WatchParentProcess, defaultSettings.WatchParentProcess, out var watchParentProcess, out message)) return false;
        if (!LidGuardSettingsInteractivePromptReader.TryReadSessionTimeoutMinutesSetting(
            "Session timeout minutes",
            normalizedStoredSettings.SessionTimeoutMinutes,
            defaultSettings.SessionTimeoutMinutes,
            out var sessionTimeoutMinutes,
            out message))
            return false;
        if (!LidGuardSettingsInteractivePromptReader.TryReadServerRuntimeCleanupDelayMinutesSetting(
            "Server runtime cleanup delay minutes",
            normalizedStoredSettings.ServerRuntimeCleanupDelayMinutes,
            defaultSettings.ServerRuntimeCleanupDelayMinutes,
            out var serverRuntimeCleanupDelayMinutes,
            out message))
            return false;
        if (!LidGuardSettingsInteractivePromptReader.TryReadBooleanSetting(
            "Emergency hibernation on high temperature",
            normalizedStoredSettings.EmergencyHibernationOnHighTemperature,
            defaultSettings.EmergencyHibernationOnHighTemperature,
            out var emergencyHibernationOnHighTemperature,
            out message))
            return false;
        if (!LidGuardSettingsInteractivePromptReader.TryReadEmergencyHibernationTemperatureModeSetting(
            "Emergency hibernation temperature mode",
            normalizedStoredSettings.EmergencyHibernationTemperatureMode,
            defaultSettings.EmergencyHibernationTemperatureMode,
            out var emergencyHibernationTemperatureMode,
            out message))
            return false;
        if (!LidGuardSettingsInteractivePromptReader.TryReadEmergencyHibernationTemperatureCelsiusSetting(
            "Emergency hibernation temperature Celsius",
            normalizedStoredSettings.EmergencyHibernationTemperatureCelsius,
            defaultSettings.EmergencyHibernationTemperatureCelsius,
            out var emergencyHibernationTemperatureCelsius,
            out message))
            return false;
        if (!LidGuardSettingsInteractivePromptReader.TryReadSuspendModeSetting("Suspend mode", normalizedStoredSettings.SuspendMode, defaultSettings.SuspendMode, out var suspendMode, out message)) return false;
        if (!LidGuardSettingsInteractivePromptReader.TryReadNonNegativeIntegerSetting(
            "Post-stop suspend delay seconds",
            normalizedStoredSettings.PostStopSuspendDelaySeconds,
            defaultSettings.PostStopSuspendDelaySeconds,
            out var postStopSuspendDelaySeconds,
            out message))
            return false;
        if (!LidGuardSettingsInteractivePromptReader.TryReadPostStopSuspendSoundSetting(
            "Post-stop suspend sound",
            normalizedStoredSettings.PostStopSuspendSound,
            defaultSettings.PostStopSuspendSound,
            out var postStopSuspendSound,
            out message))
            return false;
        if (!LidGuardSettingsInteractivePromptReader.TryReadPostStopSuspendSoundVolumeOverridePercentSetting(
            "Post-stop suspend sound volume override percent",
            normalizedStoredSettings.PostStopSuspendSoundVolumeOverridePercent,
            defaultSettings.PostStopSuspendSoundVolumeOverridePercent,
            out var postStopSuspendSoundVolumeOverridePercent,
            out message))
            return false;
        if (!LidGuardSettingsInteractivePromptReader.TryReadSuspendHistoryEntryCountSetting(
            "Suspend history entry count",
            normalizedStoredSettings.SuspendHistoryEntryCount,
            defaultSettings.SuspendHistoryEntryCount,
            out var suspendHistoryEntryCount,
            out message))
            return false;
        if (!LidGuardSettingsInteractivePromptReader.TryReadClosedLidPermissionRequestDecisionSetting(
            "Closed lid permission request decision",
            normalizedStoredSettings.ClosedLidPermissionRequestDecision,
            defaultSettings.ClosedLidPermissionRequestDecision,
            out var closedLidPermissionRequestDecision,
            out message))
            return false;

        settings = new LidGuardSettings
        {
            PowerRequest = new PowerRequestOptions
            {
                PreventSystemSleep = preventSystemSleep,
                PreventAwayModeSleep = preventAwayModeSleep,
                PreventDisplaySleep = preventDisplaySleep,
                Reason = storedPowerRequest.Reason
            },
            ChangeLidAction = changeLidAction,
            SuspendMode = suspendMode,
            PostStopSuspendDelaySeconds = postStopSuspendDelaySeconds,
            PostStopSuspendSound = postStopSuspendSound,
            PostStopSuspendSoundVolumeOverridePercent = postStopSuspendSoundVolumeOverridePercent,
            SuspendHistoryEntryCount = suspendHistoryEntryCount,
            PreSuspendWebhookUrl = normalizedStoredSettings.PreSuspendWebhookUrl,
            PostSessionEndWebhookUrl = normalizedStoredSettings.PostSessionEndWebhookUrl,
            ClosedLidPermissionRequestDecision = closedLidPermissionRequestDecision,
            WatchParentProcess = watchParentProcess,
            SessionTimeoutMinutes = sessionTimeoutMinutes,
            ServerRuntimeCleanupDelayMinutes = serverRuntimeCleanupDelayMinutes,
            EmergencyHibernationOnHighTemperature = emergencyHibernationOnHighTemperature,
            EmergencyHibernationTemperatureMode = emergencyHibernationTemperatureMode,
            EmergencyHibernationTemperatureCelsius = emergencyHibernationTemperatureCelsius
        };

        return true;
    }
}
