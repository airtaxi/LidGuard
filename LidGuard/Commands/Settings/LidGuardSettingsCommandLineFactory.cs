using LidGuardLib.Commons.Power;
using LidGuardLib.Commons.Settings;

namespace LidGuard.Commands;

internal static class LidGuardSettingsCommandLineFactory
{
    public static bool TryCreateSettings(
        IReadOnlyDictionary<string, string> options,
        LidGuardSettings currentSettings,
        out LidGuardSettings settings,
        out string message)
    {
        settings = LidGuardSettings.Normalize(currentSettings);
        if (!CommandOptionReader.TryParseBooleanOption(options, false, out var resetSettings, out message, "reset", "default", "defaults")) return false;

        var baseSettings = resetSettings ? LidGuardSettings.HeadlessRuntimeDefault : settings;
        var basePowerRequest = baseSettings.PowerRequest ?? PowerRequestOptions.Default;
        settings = baseSettings;
        message = string.Empty;

        if (!CommandOptionReader.TryParseBooleanOption(options, basePowerRequest.PreventSystemSleep, out var preventSystemSleep, out message, "prevent-system-sleep", "system-required")) return false;
        if (!CommandOptionReader.TryParseBooleanOption(options, basePowerRequest.PreventAwayModeSleep, out var preventAwayModeSleep, out message, "prevent-away-mode-sleep", "away-mode-required")) return false;
        if (!CommandOptionReader.TryParseBooleanOption(options, basePowerRequest.PreventDisplaySleep, out var preventDisplaySleep, out message, "prevent-display-sleep", "display-required")) return false;
        if (!CommandOptionReader.TryParseBooleanOption(options, baseSettings.ChangeLidAction, out var changeLidAction, out message, "change-lid-action", "lid-action")) return false;
        if (!CommandOptionReader.TryParseBooleanOption(options, baseSettings.WatchParentProcess, out var watchParentProcess, out message, "watch-parent-process", "watch-parent")) return false;
        if (!LidGuardSettingsValueParser.TryParseSessionTimeoutMinutesOption(
            options,
            baseSettings.SessionTimeoutMinutes,
            out var sessionTimeoutMinutes,
            out message))
            return false;
        if (!LidGuardSettingsValueParser.TryParseServerRuntimeCleanupDelayMinutesOption(
            options,
            baseSettings.ServerRuntimeCleanupDelayMinutes,
            out var serverRuntimeCleanupDelayMinutes,
            out message))
            return false;
        if (!CommandOptionReader.TryParseBooleanOption(
            options,
            baseSettings.EmergencyHibernationOnHighTemperature,
            out var emergencyHibernationOnHighTemperature,
            out message,
            "emergency-hibernation-on-high-temperature"))
            return false;
        if (!LidGuardSettingsValueParser.TryParseEmergencyHibernationTemperatureModeOption(
            options,
            baseSettings.EmergencyHibernationTemperatureMode,
            out var emergencyHibernationTemperatureMode,
            out message))
            return false;
        if (!LidGuardSettingsValueParser.TryParseEmergencyHibernationTemperatureCelsiusOption(
            options,
            baseSettings.EmergencyHibernationTemperatureCelsius,
            out var emergencyHibernationTemperatureCelsius,
            out message))
            return false;
        if (!LidGuardSettingsValueParser.TryParseSuspendModeOption(options, baseSettings.SuspendMode, out var suspendMode, out message)) return false;
        if (!LidGuardSettingsValueParser.TryParsePostStopSuspendDelaySecondsOption(options, baseSettings.PostStopSuspendDelaySeconds, out var postStopSuspendDelaySeconds, out message)) return false;
        if (!LidGuardSettingsValueParser.TryParsePostStopSuspendSoundVolumeOverridePercentOption(
            options,
            baseSettings.PostStopSuspendSoundVolumeOverridePercent,
            out var postStopSuspendSoundVolumeOverridePercent,
            out message))
            return false;
        if (!LidGuardSettingsValueParser.TryParseSuspendHistoryEntryCountOption(
            options,
            baseSettings.SuspendHistoryEntryCount,
            out var suspendHistoryEntryCount,
            out message))
            return false;
        var postStopSuspendSound = baseSettings.PostStopSuspendSound;
        if (CommandOptionReader.TryGetOption(options, out var postStopSuspendSoundText, "post-stop-suspend-sound")) postStopSuspendSound = postStopSuspendSoundText;
        if (!LidGuardSettingsValueParser.TryParsePreSuspendWebhookUrlOption(options, baseSettings.PreSuspendWebhookUrl, out var preSuspendWebhookUrl, out message)) return false;
        if (!LidGuardSettingsValueParser.TryParseClosedLidPermissionRequestDecisionOption(options, baseSettings.ClosedLidPermissionRequestDecision, out var closedLidPermissionRequestDecision, out message)) return false;

        var reason = CommandOptionReader.GetOption(options, "power-request-reason", "reason");
        if (string.IsNullOrWhiteSpace(reason)) reason = basePowerRequest.Reason;

        settings = new LidGuardSettings
        {
            PowerRequest = new PowerRequestOptions
            {
                PreventSystemSleep = preventSystemSleep,
                PreventAwayModeSleep = preventAwayModeSleep,
                PreventDisplaySleep = preventDisplaySleep,
                Reason = reason
            },
            ChangeLidAction = changeLidAction,
            SuspendMode = suspendMode,
            PostStopSuspendDelaySeconds = postStopSuspendDelaySeconds,
            PostStopSuspendSound = postStopSuspendSound,
            PostStopSuspendSoundVolumeOverridePercent = postStopSuspendSoundVolumeOverridePercent,
            SuspendHistoryEntryCount = suspendHistoryEntryCount,
            PreSuspendWebhookUrl = preSuspendWebhookUrl,
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
