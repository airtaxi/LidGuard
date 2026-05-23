using LidGuard.Power;
using LidGuard.Localization;
using LidGuard.Settings;

namespace LidGuard.Commands;

internal static class LidGuardSettingsCommandLineFactory
{
    public static bool TryCreateSettings(IReadOnlyDictionary<string, string> options, LidGuardSettings currentSettings, out LidGuardSettings settings, out string message)
    {
        settings = LidGuardSettings.Normalize(currentSettings);
        if (!CommandOptionReader.TryParseBooleanOption(options, false, out var resetSettings, out message, "reset", "default", "defaults")) return false;

        var baseSettings = LidGuardSettings.Normalize(resetSettings ? LidGuardSettings.HeadlessRuntimeDefault : settings);
        var basePowerRequest = baseSettings.PowerRequest ?? PowerRequestOptions.Default;
        settings = baseSettings;
        message = string.Empty;

        if (!CommandOptionReader.TryParseBooleanOption(options, basePowerRequest.PreventSystemSleep, out var preventSystemSleep, out message, "prevent-system-sleep", "system-required")) return false;
#if LIDGUARD_LINUX || LIDGUARD_MACOS
        if (CommandOptionReader.TryGetOption(options, out _, "prevent-away-mode-sleep", "away-mode-required"))
        {
            message = LocalizationService.GetString(
                "SettingsOptionPreventAwayModeSleepUnsupported");
            return false;
        }

        var preventAwayModeSleep = false;
#else
        if (!CommandOptionReader.TryParseBooleanOption(options, basePowerRequest.PreventAwayModeSleep, out var preventAwayModeSleep, out message, "prevent-away-mode-sleep", "away-mode-required")) return false;
#endif
        if (!CommandOptionReader.TryParseBooleanOption(options, basePowerRequest.PreventDisplaySleep, out var preventDisplaySleep, out message, "prevent-display-sleep", "display-required")) return false;
        if (!CommandOptionReader.TryParseBooleanOption(options, baseSettings.ChangeLidAction, out var changeLidAction, out message, "change-lid-action", "lid-action")) return false;
        if (!CommandOptionReader.TryParseBooleanOption(options, baseSettings.WatchParentProcess, out var watchParentProcess, out message, "watch-parent-process", "watch-parent")) return false;
        if (!LidGuardSettingsValueParser.TryParseSessionTimeoutMinutesOption(options, baseSettings.SessionTimeoutMinutes, out var sessionTimeoutMinutes, out message))
            return false;
        if (!LidGuardSettingsValueParser.TryParseServerRuntimeCleanupDelayMinutesOption(options, baseSettings.ServerRuntimeCleanupDelayMinutes, out var serverRuntimeCleanupDelayMinutes, out message))
            return false;
        if (!CommandOptionReader.TryParseBooleanOption(options, baseSettings.EmergencyHibernationOnHighTemperature, out var emergencyHibernationOnHighTemperature, out message, "emergency-hibernation-on-high-temperature"))
            return false;
        if (!LidGuardSettingsValueParser.TryParseEmergencyHibernationTemperatureModeOption(options, baseSettings.EmergencyHibernationTemperatureMode, out var emergencyHibernationTemperatureMode, out message))
            return false;
        if (!LidGuardSettingsValueParser.TryParseEmergencyHibernationTemperatureCelsiusOption(options, baseSettings.EmergencyHibernationTemperatureCelsius, out var emergencyHibernationTemperatureCelsius, out message))
            return false;
        if (!LidGuardSettingsValueParser.TryParseSuspendModeOption(options, baseSettings.SuspendMode, out var suspendMode, out message)) return false;
        if (!LidGuardSettingsValueParser.TryParsePostStopSuspendDelaySecondsOption(options, baseSettings.PostStopSuspendDelaySeconds, out var postStopSuspendDelaySeconds, out message)) return false;
        if (!LidGuardSettingsValueParser.TryParseClosedLidStopFollowUpDelaySecondsOption(options, baseSettings.ClosedLidStopFollowUpDelaySeconds, out var closedLidStopFollowUpDelaySeconds, out message))
            return false;
        if (!LidGuardSettingsValueParser.TryParsePostStopSuspendSoundVolumeOverridePercentOption(options, baseSettings.PostStopSuspendSoundVolumeOverridePercent, out var postStopSuspendSoundVolumeOverridePercent, out message)) return false;
        if (!LidGuardSettingsValueParser.TryParseClosedLidStopFollowUpSoundVolumeOverridePercentOption(options, baseSettings.ClosedLidStopFollowUpSoundVolumeOverridePercent, out var closedLidStopFollowUpSoundVolumeOverridePercent, out message)) return false;
        if (!LidGuardSettingsValueParser.TryParseSuspendHistoryEntryCountOption(options, baseSettings.SuspendHistoryEntryCount, out var suspendHistoryEntryCount, out message)) return false;
        var postStopSuspendSound = baseSettings.PostStopSuspendSound;
        if (CommandOptionReader.TryGetOption(options, out var postStopSuspendSoundText, "post-stop-suspend-sound")) postStopSuspendSound = postStopSuspendSoundText;
        var closedLidStopFollowUpSound = baseSettings.ClosedLidStopFollowUpSound;
        if (CommandOptionReader.TryGetOption(options, out var closedLidStopFollowUpSoundText, "closed-lid-stop-follow-up-sound")) closedLidStopFollowUpSound = closedLidStopFollowUpSoundText;
        if (!LidGuardSettingsValueParser.TryParsePreSuspendWebhookUrlOption(options, baseSettings.PreSuspendWebhookUrl, out var preSuspendWebhookUrl, out message)) return false;
        if (!LidGuardSettingsValueParser.TryParsePostSessionEndWebhookUrlOption(options, baseSettings.PostSessionEndWebhookUrl, out var postSessionEndWebhookUrl, out message)) return false;
        if (!LidGuardSettingsValueParser.TryParseClosedLidStopFollowUpWebhookUrlOption(options, baseSettings.ClosedLidStopFollowUpWebhookUrl, out var closedLidStopFollowUpWebhookUrl, out message)) return false;
        if (!CommandOptionReader.TryParseBooleanOption(options, baseSettings.RepeatClosedLidStopFollowUp, out var repeatClosedLidStopFollowUp, out message, "repeat-closed-lid-stop-follow-up")) return false;
        if (!LidGuardSettingsValueParser.TryParseClosedLidPermissionRequestDecisionOption(options, baseSettings.ClosedLidPermissionRequestDecision, out var closedLidPermissionRequestDecision, out message)) return false;
        if (!LidGuardSettingsValueParser.TryParseUserInterfaceCultureOption(options, baseSettings.UserInterfaceCulture, out var userInterfaceCulture, out message)) return false;

        var reason = CommandOptionReader.GetOption(options, "power-request-reason", "reason");
        if (string.IsNullOrWhiteSpace(reason)) reason = basePowerRequest.Reason;

        settings = baseSettings with
        {
            PowerRequest = basePowerRequest with
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
            ClosedLidStopFollowUpSound = closedLidStopFollowUpSound,
            ClosedLidStopFollowUpSoundVolumeOverridePercent = closedLidStopFollowUpSoundVolumeOverridePercent,
            SuspendHistoryEntryCount = suspendHistoryEntryCount,
            PreSuspendWebhookUrl = preSuspendWebhookUrl,
            PostSessionEndWebhookUrl = postSessionEndWebhookUrl,
            ClosedLidStopFollowUpWebhookUrl = closedLidStopFollowUpWebhookUrl,
            ClosedLidStopFollowUpDelaySeconds = closedLidStopFollowUpDelaySeconds,
            RepeatClosedLidStopFollowUp = repeatClosedLidStopFollowUp,
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
