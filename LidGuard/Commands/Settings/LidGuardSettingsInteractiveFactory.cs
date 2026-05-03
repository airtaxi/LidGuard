using LidGuard.Power;
using LidGuard.Settings;
using LidGuard.Localization;

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

        if (!LidGuardSettingsInteractivePromptReader.TryReadBooleanSetting(LidGuardText.SettingsNamePreventSystemSleep, storedPowerRequest.PreventSystemSleep, defaultPowerRequest.PreventSystemSleep, out var preventSystemSleep, out message)) return false;
#if LIDGUARD_LINUX || LIDGUARD_MACOS
        var preventAwayModeSleep = false;
#else
        if (!LidGuardSettingsInteractivePromptReader.TryReadBooleanSetting(LidGuardText.SettingsNamePreventAwayModeSleep, storedPowerRequest.PreventAwayModeSleep, defaultPowerRequest.PreventAwayModeSleep, out var preventAwayModeSleep, out message)) return false;
#endif
        if (!LidGuardSettingsInteractivePromptReader.TryReadBooleanSetting(LidGuardText.SettingsNamePreventDisplaySleep, storedPowerRequest.PreventDisplaySleep, defaultPowerRequest.PreventDisplaySleep, out var preventDisplaySleep, out message)) return false;
        if (!LidGuardSettingsInteractivePromptReader.TryReadBooleanSetting(LidGuardText.SettingsNameChangeLidAction, normalizedStoredSettings.ChangeLidAction, defaultSettings.ChangeLidAction, out var changeLidAction, out message)) return false;
        if (!LidGuardSettingsInteractivePromptReader.TryReadBooleanSetting(LidGuardText.SettingsNameWatchParentProcess, normalizedStoredSettings.WatchParentProcess, defaultSettings.WatchParentProcess, out var watchParentProcess, out message)) return false;
        if (!LidGuardSettingsInteractivePromptReader.TryReadSessionTimeoutMinutesSetting(
            LidGuardText.SettingsNameSessionTimeoutMinutes,
            normalizedStoredSettings.SessionTimeoutMinutes,
            defaultSettings.SessionTimeoutMinutes,
            out var sessionTimeoutMinutes,
            out message))
            return false;
        if (!LidGuardSettingsInteractivePromptReader.TryReadServerRuntimeCleanupDelayMinutesSetting(
            LidGuardText.SettingsNameServerRuntimeCleanupDelayMinutes,
            normalizedStoredSettings.ServerRuntimeCleanupDelayMinutes,
            defaultSettings.ServerRuntimeCleanupDelayMinutes,
            out var serverRuntimeCleanupDelayMinutes,
            out message))
            return false;
        if (!LidGuardSettingsInteractivePromptReader.TryReadBooleanSetting(
            LidGuardText.SettingsNameEmergencyHibernationOnHighTemperature,
            normalizedStoredSettings.EmergencyHibernationOnHighTemperature,
            defaultSettings.EmergencyHibernationOnHighTemperature,
            out var emergencyHibernationOnHighTemperature,
            out message))
            return false;
        if (!LidGuardSettingsInteractivePromptReader.TryReadEmergencyHibernationTemperatureModeSetting(
            LidGuardText.SettingsNameEmergencyHibernationTemperatureMode,
            normalizedStoredSettings.EmergencyHibernationTemperatureMode,
            defaultSettings.EmergencyHibernationTemperatureMode,
            out var emergencyHibernationTemperatureMode,
            out message))
            return false;
        if (!LidGuardSettingsInteractivePromptReader.TryReadEmergencyHibernationTemperatureCelsiusSetting(
            LidGuardText.SettingsNameEmergencyHibernationTemperatureCelsius,
            normalizedStoredSettings.EmergencyHibernationTemperatureCelsius,
            defaultSettings.EmergencyHibernationTemperatureCelsius,
            out var emergencyHibernationTemperatureCelsius,
            out message))
            return false;
        if (!LidGuardSettingsInteractivePromptReader.TryReadSuspendModeSetting(LidGuardText.SettingsNameSuspendMode, normalizedStoredSettings.SuspendMode, defaultSettings.SuspendMode, out var suspendMode, out message)) return false;
        if (!LidGuardSettingsInteractivePromptReader.TryReadNonNegativeIntegerSetting(
            LidGuardText.SettingsNamePostStopSuspendDelaySeconds,
            normalizedStoredSettings.PostStopSuspendDelaySeconds,
            defaultSettings.PostStopSuspendDelaySeconds,
            out var postStopSuspendDelaySeconds,
            out message))
            return false;
        if (!LidGuardSettingsInteractivePromptReader.TryReadPostStopSuspendSoundSetting(
            LidGuardText.SettingsNamePostStopSuspendSound,
            normalizedStoredSettings.PostStopSuspendSound,
            defaultSettings.PostStopSuspendSound,
            out var postStopSuspendSound,
            out message))
            return false;
        if (!LidGuardSettingsInteractivePromptReader.TryReadPostStopSuspendSoundVolumeOverridePercentSetting(
            LidGuardText.SettingsNamePostStopSuspendSoundVolumeOverridePercent,
            normalizedStoredSettings.PostStopSuspendSoundVolumeOverridePercent,
            defaultSettings.PostStopSuspendSoundVolumeOverridePercent,
            out var postStopSuspendSoundVolumeOverridePercent,
            out message))
            return false;
        if (!LidGuardSettingsInteractivePromptReader.TryReadSuspendHistoryEntryCountSetting(
            LidGuardText.SettingsNameSuspendHistoryEntryCount,
            normalizedStoredSettings.SuspendHistoryEntryCount,
            defaultSettings.SuspendHistoryEntryCount,
            out var suspendHistoryEntryCount,
            out message))
            return false;
        if (!LidGuardSettingsInteractivePromptReader.TryReadClosedLidPermissionRequestDecisionSetting(
            LidGuardText.SettingsNameClosedLidPermissionRequestDecision,
            normalizedStoredSettings.ClosedLidPermissionRequestDecision,
            defaultSettings.ClosedLidPermissionRequestDecision,
            out var closedLidPermissionRequestDecision,
            out message))
            return false;
        if (!LidGuardSettingsInteractivePromptReader.TryReadUserInterfaceCultureSetting(
            LidGuardText.SettingsNameUserInterfaceCulture,
            normalizedStoredSettings.UserInterfaceCulture,
            defaultSettings.UserInterfaceCulture,
            out var userInterfaceCulture,
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
            EmergencyHibernationTemperatureCelsius = emergencyHibernationTemperatureCelsius,
            UserInterfaceCulture = userInterfaceCulture
        };

        return true;
    }
}
