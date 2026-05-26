using LidGuard.Ipc;
using LidGuard.Settings;
using LidGuard.Power;
using LidGuard.Localization;

namespace LidGuard.Commands;

internal static class LidGuardSettingsValueParser
{
    public static bool TryParsePreSuspendWebhookUrlOption(IReadOnlyDictionary<string, string> options, string defaultValue, out string preSuspendWebhookUrl, out string message)
    {
        preSuspendWebhookUrl = defaultValue;
        message = string.Empty;
        if (!CommandOptionReader.TryGetOption(options, out var preSuspendWebhookUrlText, "pre-suspend-webhook-url", "suspend-webhook-url")) return true;

        if (string.IsNullOrWhiteSpace(preSuspendWebhookUrlText) || preSuspendWebhookUrlText.Trim().Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            message = LocalizationService.GetFormattedString("SettingsOptionPreSuspendWebhookRemovalCommand", LidGuardCommandConsole.GetCommandDisplayName(), LidGuardPipeCommands.RemovePreSuspendWebhook);
            return false;
        }

        return PreSuspendWebhookConfiguration.TryNormalizeConfiguredValue(preSuspendWebhookUrlText, out preSuspendWebhookUrl, out message);
    }

    public static bool TryParsePostSessionEndWebhookUrlOption(IReadOnlyDictionary<string, string> options, string defaultValue, out string postSessionEndWebhookUrl, out string message)
    {
        postSessionEndWebhookUrl = defaultValue;
        message = string.Empty;
        if (!CommandOptionReader.TryGetOption(options, out var postSessionEndWebhookUrlText, "post-session-end-webhook-url")) return true;

        if (string.IsNullOrWhiteSpace(postSessionEndWebhookUrlText) || postSessionEndWebhookUrlText.Trim().Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            message = LocalizationService.GetFormattedString("SettingsOptionPostSessionEndWebhookRemovalCommand", LidGuardCommandConsole.GetCommandDisplayName(), LidGuardPipeCommands.RemovePostSessionEndWebhook);
            return false;
        }

        return PostSessionEndWebhookConfiguration.TryNormalizeConfiguredValue(postSessionEndWebhookUrlText, out postSessionEndWebhookUrl, out message);
    }

    public static bool TryParseClosedLidStopFollowUpWebhookUrlOption(IReadOnlyDictionary<string, string> options, string defaultValue, out string closedLidStopFollowUpWebhookUrl, out string message)
    {
        closedLidStopFollowUpWebhookUrl = defaultValue;
        message = string.Empty;
        if (!CommandOptionReader.TryGetOption(options, out var closedLidStopFollowUpWebhookUrlText, "closed-lid-stop-follow-up-webhook-url")) return true;

        if (string.IsNullOrWhiteSpace(closedLidStopFollowUpWebhookUrlText) || closedLidStopFollowUpWebhookUrlText.Trim().Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            message = LocalizationService.GetFormattedString("SettingsOptionClosedLidStopFollowUpWebhookRemovalCommand", LidGuardCommandConsole.GetCommandDisplayName(), LidGuardPipeCommands.RemoveClosedLidStopFollowUpWebhook);
            return false;
        }

        return ClosedLidStopFollowUpWebhookConfiguration.TryNormalizeConfiguredValue(closedLidStopFollowUpWebhookUrlText, out closedLidStopFollowUpWebhookUrl, out message);
    }

    public static bool TryParseClosedLidPermissionRequestDecisionOption(IReadOnlyDictionary<string, string> options, ClosedLidPermissionRequestDecision defaultValue, out ClosedLidPermissionRequestDecision closedLidPermissionRequestDecision, out string message)
    {
        closedLidPermissionRequestDecision = defaultValue;
        message = string.Empty;
        if (!CommandOptionReader.TryGetOption(options, out var permissionRequestDecisionText, "closed-lid-permission-request-decision", "permission-request-decision-when-lid-closed")) return true;
        return TryParseClosedLidPermissionRequestDecision(permissionRequestDecisionText, out closedLidPermissionRequestDecision, out message);
    }

    public static bool TryParseUserInterfaceCultureOption(IReadOnlyDictionary<string, string> options, string defaultValue, out string userInterfaceCulture, out string message)
    {
        userInterfaceCulture = defaultValue;
        message = string.Empty;
        if (!CommandOptionReader.TryGetOption(options, out var userInterfaceCultureText, "ui-culture", "user-interface-culture")) return true;
        return UserInterfaceCultureConfiguration.TryNormalizeConfiguredValue(userInterfaceCultureText, out userInterfaceCulture, out message);
    }

    public static bool TryParseClosedLidPermissionRequestDecision(string permissionRequestDecisionText, out ClosedLidPermissionRequestDecision closedLidPermissionRequestDecision, out string message)
    {
        closedLidPermissionRequestDecision = ClosedLidPermissionRequestDecision.Deny;
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(permissionRequestDecisionText))
        {
            message = LocalizationService.GetString("SettingsOptionClosedLidPermissionRequestDecisionValidation");
            return false;
        }

        switch (permissionRequestDecisionText.Trim().ToLowerInvariant())
        {
            case "allow":
                closedLidPermissionRequestDecision = ClosedLidPermissionRequestDecision.Allow;
                return true;
            case "ask":
                closedLidPermissionRequestDecision = ClosedLidPermissionRequestDecision.Ask;
                return true;
            case "deny":
                closedLidPermissionRequestDecision = ClosedLidPermissionRequestDecision.Deny;
                return true;
            default:
                message = LocalizationService.GetString("SettingsOptionClosedLidPermissionRequestDecisionValidation");
                return false;
        }
    }

    public static bool TryParseSuspendModeOption(IReadOnlyDictionary<string, string> options, SystemSuspendMode defaultValue, out SystemSuspendMode suspendMode, out string message)
    {
        suspendMode = defaultValue;
        message = string.Empty;
        if (!CommandOptionReader.TryGetOption(options, out var suspendModeText, "suspend-mode")) return true;

        var normalizedSuspendModeText = suspendModeText.Trim();
        suspendMode = normalizedSuspendModeText.ToLowerInvariant() switch
        {
            "sleep" => SystemSuspendMode.Sleep,
            "hibernate" => SystemSuspendMode.Hibernate,
            _ => defaultValue
        };

        if (suspendMode == defaultValue && !normalizedSuspendModeText.Equals(defaultValue.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            message = LocalizationService.GetString("SettingsOptionSuspendModeValidation");
            return false;
        }

        return true;
    }

    public static bool TryParsePostStopSuspendDelaySecondsOption(IReadOnlyDictionary<string, string> options, int defaultValue, out int postStopSuspendDelaySeconds, out string message)
    {
        postStopSuspendDelaySeconds = defaultValue;
        message = string.Empty;
        if (!CommandOptionReader.TryGetOption(options, out var postStopSuspendDelaySecondsText, "post-stop-suspend-delay-seconds")) return true;
        if (int.TryParse(postStopSuspendDelaySecondsText.Trim(), out postStopSuspendDelaySeconds) && postStopSuspendDelaySeconds >= 0) return true;

        message = LocalizationService.GetString("SettingsOptionPostStopSuspendDelaySecondsValidation");
        return false;
    }

    public static bool TryParseClosedLidStopFollowUpDelaySecondsOption(IReadOnlyDictionary<string, string> options, int defaultValue, out int closedLidStopFollowUpDelaySeconds, out string message)
    {
        closedLidStopFollowUpDelaySeconds = defaultValue;
        message = string.Empty;
        if (!CommandOptionReader.TryGetOption(options, out var closedLidStopFollowUpDelaySecondsText, "closed-lid-stop-follow-up-delay-seconds")) return true;
        if (int.TryParse(closedLidStopFollowUpDelaySecondsText.Trim(), out closedLidStopFollowUpDelaySeconds) && closedLidStopFollowUpDelaySeconds >= 0) return true;

        message = LocalizationService.GetString("SettingsOptionClosedLidStopFollowUpDelaySecondsValidation");
        return false;
    }

    public static bool TryParsePostStopSuspendSoundVolumeOverridePercentOption(IReadOnlyDictionary<string, string> options, int? defaultValue, out int? postStopSuspendSoundVolumeOverridePercent, out string message)
    {
        postStopSuspendSoundVolumeOverridePercent = defaultValue;
        message = string.Empty;
        if (!CommandOptionReader.TryGetOption(options, out var postStopSuspendSoundVolumeOverridePercentText, "post-stop-suspend-sound-volume-override-percent")) return true;

        if (string.IsNullOrWhiteSpace(postStopSuspendSoundVolumeOverridePercentText) || postStopSuspendSoundVolumeOverridePercentText.Trim().Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            postStopSuspendSoundVolumeOverridePercent = null;
            return true;
        }

        if (int.TryParse(postStopSuspendSoundVolumeOverridePercentText.Trim(), out var parsedValue) && LidGuardSettings.IsValidPostStopSuspendSoundVolumeOverridePercent(parsedValue))
        {
            postStopSuspendSoundVolumeOverridePercent = parsedValue;
            return true;
        }

        message = LocalizationService.GetFormattedString("SettingsOptionPostStopSuspendSoundVolumeOverrideValidation", LidGuardSettings.MinimumPostStopSuspendSoundVolumeOverridePercent, LidGuardSettings.MaximumPostStopSuspendSoundVolumeOverridePercent);
        return false;
    }

    public static bool TryParseClosedLidStopFollowUpSoundVolumeOverridePercentOption(IReadOnlyDictionary<string, string> options, int? defaultValue, out int? closedLidStopFollowUpSoundVolumeOverridePercent, out string message)
    {
        closedLidStopFollowUpSoundVolumeOverridePercent = defaultValue;
        message = string.Empty;
        if (!CommandOptionReader.TryGetOption(options, out var closedLidStopFollowUpSoundVolumeOverridePercentText, "closed-lid-stop-follow-up-sound-volume-override-percent", "override")) return true;

        if (string.IsNullOrWhiteSpace(closedLidStopFollowUpSoundVolumeOverridePercentText) || closedLidStopFollowUpSoundVolumeOverridePercentText.Trim().Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            closedLidStopFollowUpSoundVolumeOverridePercent = null;
            return true;
        }

        if (int.TryParse(closedLidStopFollowUpSoundVolumeOverridePercentText.Trim(), out var parsedValue) && LidGuardSettings.IsValidClosedLidStopFollowUpSoundVolumeOverridePercent(parsedValue))
        {
            closedLidStopFollowUpSoundVolumeOverridePercent = parsedValue;
            return true;
        }

        message = LocalizationService.GetFormattedString("SettingsOptionClosedLidStopFollowUpSoundVolumeOverrideValidation", LidGuardSettings.MinimumPostStopSuspendSoundVolumeOverridePercent, LidGuardSettings.MaximumPostStopSuspendSoundVolumeOverridePercent);
        return false;
    }

    public static bool TryParseSuspendHistoryEntryCountOption(IReadOnlyDictionary<string, string> options, int? defaultValue, out int? suspendHistoryEntryCount, out string message)
    {
        suspendHistoryEntryCount = defaultValue;
        message = string.Empty;
        if (!CommandOptionReader.TryGetOption(options, out var suspendHistoryEntryCountText, "suspend-history-count", "suspend-history-entry-count")) return true;

        if (string.IsNullOrWhiteSpace(suspendHistoryEntryCountText) || suspendHistoryEntryCountText.Trim().Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            suspendHistoryEntryCount = null;
            return true;
        }

        if (int.TryParse(suspendHistoryEntryCountText.Trim(), out var parsedValue) && LidGuardSettings.IsValidSuspendHistoryEntryCount(parsedValue))
        {
            suspendHistoryEntryCount = parsedValue;
            return true;
        }

        message = LocalizationService.GetFormattedString("SettingsOptionSuspendHistoryCountValidation", LidGuardSettings.MinimumSuspendHistoryEntryCount);
        return false;
    }

    public static bool TryParseSessionTimeoutMinutesOption(IReadOnlyDictionary<string, string> options, int? defaultValue, out int? sessionTimeoutMinutes, out string message)
    {
        sessionTimeoutMinutes = defaultValue;
        message = string.Empty;
        if (!CommandOptionReader.TryGetOption(options, out var sessionTimeoutMinutesText, "session-timeout-minutes")) return true;

        if (string.IsNullOrWhiteSpace(sessionTimeoutMinutesText) || sessionTimeoutMinutesText.Trim().Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            sessionTimeoutMinutes = null;
            return true;
        }

        if (int.TryParse(sessionTimeoutMinutesText.Trim(), out var parsedValue) && LidGuardSettings.IsValidSessionTimeoutMinutes(parsedValue))
        {
            sessionTimeoutMinutes = parsedValue;
            return true;
        }

        message = LocalizationService.GetFormattedString("SettingsOptionSessionTimeoutValidation", LidGuardSettings.MinimumSessionTimeoutMinutes);
        return false;
    }

    public static bool TryParseServerRuntimeCleanupDelayMinutesOption(IReadOnlyDictionary<string, string> options, int? defaultValue, out int? serverRuntimeCleanupDelayMinutes, out string message)
    {
        serverRuntimeCleanupDelayMinutes = defaultValue;
        message = string.Empty;
        if (!CommandOptionReader.TryGetOption(options, out var serverRuntimeCleanupDelayMinutesText, "server-runtime-cleanup-delay-minutes")) return true;

        if (string.IsNullOrWhiteSpace(serverRuntimeCleanupDelayMinutesText) || serverRuntimeCleanupDelayMinutesText.Trim().Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            serverRuntimeCleanupDelayMinutes = null;
            return true;
        }

        if (int.TryParse(serverRuntimeCleanupDelayMinutesText.Trim(), out var parsedValue) && LidGuardSettings.IsValidServerRuntimeCleanupDelayMinutes(parsedValue))
        {
            serverRuntimeCleanupDelayMinutes = parsedValue;
            return true;
        }

        message = LocalizationService.GetFormattedString("SettingsOptionServerRuntimeCleanupDelayValidation", LidGuardSettings.MinimumServerRuntimeCleanupDelayMinutes);
        return false;
    }

    public static bool TryParseEmergencyHibernationTemperatureCelsiusOption(IReadOnlyDictionary<string, string> options, int defaultValue, out int emergencyHibernationTemperatureCelsius, out string message)
    {
        emergencyHibernationTemperatureCelsius = defaultValue;
        message = string.Empty;
        if (!CommandOptionReader.TryGetOption(options, out var emergencyHibernationTemperatureCelsiusText, "emergency-hibernation-temperature-celsius")) return true;
        if (int.TryParse(emergencyHibernationTemperatureCelsiusText.Trim(), out emergencyHibernationTemperatureCelsius) && emergencyHibernationTemperatureCelsius >= LidGuardSettings.MinimumEmergencyHibernationTemperatureCelsius && emergencyHibernationTemperatureCelsius <= LidGuardSettings.MaximumEmergencyHibernationTemperatureCelsius)
            return true;

        message = LocalizationService.GetFormattedString("SettingsOptionEmergencyHibernationTemperatureCelsiusValidation", LidGuardSettings.MinimumEmergencyHibernationTemperatureCelsius, LidGuardSettings.MaximumEmergencyHibernationTemperatureCelsius);
        return false;
    }

    public static bool TryParseEmergencyHibernationTemperatureModeOption(IReadOnlyDictionary<string, string> options, EmergencyHibernationTemperatureMode defaultValue, out EmergencyHibernationTemperatureMode emergencyHibernationTemperatureMode, out string message)
    {
        emergencyHibernationTemperatureMode = defaultValue;
        message = string.Empty;
        if (!CommandOptionReader.TryGetOption(options, out var emergencyHibernationTemperatureModeText, "emergency-hibernation-temperature-mode")) return true;
        if (TryParseEmergencyHibernationTemperatureMode(emergencyHibernationTemperatureModeText, out emergencyHibernationTemperatureMode)) return true;

        message = LocalizationService.GetString("SettingsOptionEmergencyHibernationTemperatureModeValidation");
        return false;
    }

    public static bool TryParseEmergencyHibernationTemperatureMode(string emergencyHibernationTemperatureModeText, out EmergencyHibernationTemperatureMode emergencyHibernationTemperatureMode)
    {
        emergencyHibernationTemperatureMode = EmergencyHibernationTemperatureMode.Average;
        if (string.IsNullOrWhiteSpace(emergencyHibernationTemperatureModeText)) return false;

        switch (emergencyHibernationTemperatureModeText.Trim().ToLowerInvariant())
        {
            case "low":
                emergencyHibernationTemperatureMode = EmergencyHibernationTemperatureMode.Low;
                return true;
            case "average":
                emergencyHibernationTemperatureMode = EmergencyHibernationTemperatureMode.Average;
                return true;
            case "high":
                emergencyHibernationTemperatureMode = EmergencyHibernationTemperatureMode.High;
                return true;
            default:
                return false;
        }
    }

    public static bool TryParseInteractiveBoolean(string valueText, out bool value)
    {
        value = false;
        if (valueText.Equals(bool.TrueString, StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (valueText.Equals(bool.FalseString, StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        return false;
    }
}
