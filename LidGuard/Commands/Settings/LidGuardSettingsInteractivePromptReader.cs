using LidGuard.Settings;
using LidGuard.Power;
using LidGuard.Localization;

namespace LidGuard.Commands;

internal static class LidGuardSettingsInteractivePromptReader
{
    public static bool TryReadBooleanSetting(string settingName, bool storedValue, bool defaultValue, out bool value, out string message)
    {
        value = storedValue;
        message = string.Empty;
        WriteInteractiveSettingPrompt(settingName, storedValue.ToString(), defaultValue.ToString());

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = LocalizationService.GetFormattedString("SettingsInteractiveInputEnded", settingName);
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        if (LidGuardSettingsValueParser.TryParseInteractiveBoolean(valueText.Trim(), out value)) return true;

        message = LocalizationService.GetFormattedString("SettingsInteractiveBooleanValidation", settingName);
        return false;
    }

    public static bool TryReadSuspendModeSetting(
        string settingName,
        SystemSuspendMode storedValue,
        SystemSuspendMode defaultValue,
        out SystemSuspendMode value,
        out string message)
    {
        value = storedValue;
        message = string.Empty;
        WriteInteractiveSettingPrompt(
            settingName,
            LocalizationService.DisplaySuspendMode(storedValue),
            LocalizationService.DisplaySuspendMode(defaultValue),
            LocalizationService.GetString("SettingsInteractiveSuspendModeDetails", "candidates: Sleep, Hibernate"));

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = LocalizationService.GetFormattedString("SettingsInteractiveInputEnded", settingName);
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;

        var normalizedValueText = valueText.Trim();
        value = normalizedValueText.ToLowerInvariant() switch
        {
            "sleep" => SystemSuspendMode.Sleep,
            "hibernate" => SystemSuspendMode.Hibernate,
            _ => storedValue
        };

        if (normalizedValueText.Equals("sleep", StringComparison.OrdinalIgnoreCase)) return true;
        if (normalizedValueText.Equals("hibernate", StringComparison.OrdinalIgnoreCase)) return true;

        message = LocalizationService.GetFormattedString("SettingsInteractiveSuspendModeValidation", settingName);
        return false;
    }

    public static bool TryReadNonNegativeIntegerSetting(
        string settingName,
        int storedValue,
        int defaultValue,
        out int value,
        out string message)
    {
        value = storedValue;
        message = string.Empty;
        WriteInteractiveSettingPrompt(settingName, storedValue.ToString(), defaultValue.ToString(), LocalizationService.GetString("SettingsInteractiveImmediateDetails", "0 = immediate"));

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = LocalizationService.GetFormattedString("SettingsInteractiveInputEnded", settingName);
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        if (int.TryParse(valueText.Trim(), out value) && value >= 0) return true;

        message = LocalizationService.GetFormattedString("SettingsInteractiveNonNegativeIntegerValidation", settingName);
        return false;
    }

    public static bool TryReadSessionTimeoutMinutesSetting(
        string settingName,
        int? storedValue,
        int? defaultValue,
        out int? value,
        out string message)
    {
        value = storedValue;
        message = string.Empty;
        WriteInteractiveSettingPrompt(
            settingName,
            LocalizationService.DisplayMinuteCount(storedValue),
            LocalizationService.DisplayMinuteCount(defaultValue),
            LocalizationService.GetFormattedString("SettingsInteractiveSessionTimeoutDetails", LidGuardSettings.MinimumSessionTimeoutMinutes));

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = LocalizationService.GetFormattedString("SettingsInteractiveInputEnded", settingName);
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        if (valueText.Trim().Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            value = null;
            return true;
        }

        if (int.TryParse(valueText.Trim(), out var parsedValue)
            && LidGuardSettings.IsValidSessionTimeoutMinutes(parsedValue))
        {
            value = parsedValue;
            return true;
        }

        message = LocalizationService.GetFormattedString("SettingsInteractiveValueOffOrMinimumValidation", settingName, LidGuardSettings.MinimumSessionTimeoutMinutes);
        return false;
    }

    public static bool TryReadServerRuntimeCleanupDelayMinutesSetting(
        string settingName,
        int? storedValue,
        int? defaultValue,
        out int? value,
        out string message)
    {
        value = storedValue;
        message = string.Empty;
        WriteInteractiveSettingPrompt(
            settingName,
            LocalizationService.DisplayMinuteCount(storedValue),
            LocalizationService.DisplayMinuteCount(defaultValue),
            LocalizationService.GetFormattedString("SettingsInteractiveServerRuntimeCleanupDelayDetails", LidGuardSettings.MinimumServerRuntimeCleanupDelayMinutes));

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = LocalizationService.GetFormattedString("SettingsInteractiveInputEnded", settingName);
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        if (valueText.Trim().Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            value = null;
            return true;
        }

        if (int.TryParse(valueText.Trim(), out var parsedValue)
            && LidGuardSettings.IsValidServerRuntimeCleanupDelayMinutes(parsedValue))
        {
            value = parsedValue;
            return true;
        }

        message = LocalizationService.GetFormattedString("SettingsInteractiveValueOffOrMinimumValidation", settingName, LidGuardSettings.MinimumServerRuntimeCleanupDelayMinutes);
        return false;
    }

    public static bool TryReadPostStopSuspendSoundVolumeOverridePercentSetting(
        string settingName,
        int? storedValue,
        int? defaultValue,
        out int? value,
        out string message)
    {
        value = storedValue;
        message = string.Empty;
        WriteInteractiveSettingPrompt(
            settingName,
            LocalizationService.DisplayOptionalValue(PostStopSuspendSoundConfiguration.GetVolumeOverrideDisplayValue(storedValue)),
            LocalizationService.DisplayOptionalValue(PostStopSuspendSoundConfiguration.GetVolumeOverrideDisplayValue(defaultValue)),
            LocalizationService.GetFormattedString("SettingsInteractiveVolumeOverrideDetails",
                LidGuardSettings.MinimumPostStopSuspendSoundVolumeOverridePercent,
                LidGuardSettings.MaximumPostStopSuspendSoundVolumeOverridePercent));

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = LocalizationService.GetFormattedString("SettingsInteractiveInputEnded", settingName);
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        if (valueText.Trim().Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            value = null;
            return true;
        }

        if (int.TryParse(valueText.Trim(), out var parsedValue)
            && LidGuardSettings.IsValidPostStopSuspendSoundVolumeOverridePercent(parsedValue))
        {
            value = parsedValue;
            return true;
        }

        message = LocalizationService.GetFormattedString("SettingsInteractiveVolumeOverrideValidation",
            settingName,
            LidGuardSettings.MinimumPostStopSuspendSoundVolumeOverridePercent,
            LidGuardSettings.MaximumPostStopSuspendSoundVolumeOverridePercent);
        return false;
    }

    public static bool TryReadSuspendHistoryEntryCountSetting(
        string settingName,
        int? storedValue,
        int? defaultValue,
        out int? value,
        out string message)
    {
        value = storedValue;
        message = string.Empty;
        WriteInteractiveSettingPrompt(
            settingName,
            LocalizationService.DisplaySuspendHistoryEntryCount(storedValue),
            LocalizationService.DisplaySuspendHistoryEntryCount(defaultValue),
            LocalizationService.GetFormattedString("SettingsInteractiveSuspendHistoryEntryCountDetails", LidGuardSettings.MinimumSuspendHistoryEntryCount));

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = LocalizationService.GetFormattedString("SettingsInteractiveInputEnded", settingName);
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        if (valueText.Trim().Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            value = null;
            return true;
        }

        if (int.TryParse(valueText.Trim(), out var parsedValue)
            && LidGuardSettings.IsValidSuspendHistoryEntryCount(parsedValue))
        {
            value = parsedValue;
            return true;
        }

        message = LocalizationService.GetFormattedString("SettingsInteractiveValueOffOrMinimumValidation", settingName, LidGuardSettings.MinimumSuspendHistoryEntryCount);
        return false;
    }

    public static bool TryReadEmergencyHibernationTemperatureCelsiusSetting(
        string settingName,
        int storedValue,
        int defaultValue,
        out int value,
        out string message)
    {
        value = storedValue;
        message = string.Empty;
        WriteInteractiveSettingPrompt(
            settingName,
            storedValue.ToString(),
            defaultValue.ToString(),
            LocalizationService.GetFormattedString("SettingsInteractiveRangeDetails",
                LidGuardSettings.MinimumEmergencyHibernationTemperatureCelsius,
                LidGuardSettings.MaximumEmergencyHibernationTemperatureCelsius));

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = LocalizationService.GetFormattedString("SettingsInteractiveInputEnded", settingName);
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        if (int.TryParse(valueText.Trim(), out value)
            && value >= LidGuardSettings.MinimumEmergencyHibernationTemperatureCelsius
            && value <= LidGuardSettings.MaximumEmergencyHibernationTemperatureCelsius)
            return true;

        message = LocalizationService.GetFormattedString("SettingsInteractiveRangeValidation",
            settingName,
            LidGuardSettings.MinimumEmergencyHibernationTemperatureCelsius,
            LidGuardSettings.MaximumEmergencyHibernationTemperatureCelsius);
        return false;
    }

    public static bool TryReadEmergencyHibernationTemperatureModeSetting(
        string settingName,
        EmergencyHibernationTemperatureMode storedValue,
        EmergencyHibernationTemperatureMode defaultValue,
        out EmergencyHibernationTemperatureMode value,
        out string message)
    {
        value = storedValue;
        message = string.Empty;
        WriteInteractiveSettingPrompt(
            settingName,
            LocalizationService.DisplayEmergencyHibernationTemperatureMode(storedValue),
            LocalizationService.DisplayEmergencyHibernationTemperatureMode(defaultValue),
            LocalizationService.GetString("SettingsInteractiveEmergencyHibernationTemperatureModeDetails", "candidates: Low, Average, High"));

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = LocalizationService.GetFormattedString("SettingsInteractiveInputEnded", settingName);
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        if (LidGuardSettingsValueParser.TryParseEmergencyHibernationTemperatureMode(valueText, out value)) return true;

        message = LocalizationService.GetFormattedString("SettingsInteractiveEmergencyHibernationTemperatureModeValidation", settingName);
        return false;
    }

    public static bool TryReadPostStopSuspendSoundSetting(
        string settingName,
        string storedValue,
        string defaultValue,
        out string value,
        out string message)
    {
        value = storedValue;
        message = string.Empty;
        var storedDisplayValue = PostStopSuspendSoundConfiguration.GetDisplayValue(storedValue);
        var defaultDisplayValue = PostStopSuspendSoundConfiguration.GetDisplayValue(defaultValue);
        WriteInteractiveSettingPrompt(
            settingName,
            LocalizationService.DisplayOptionalValue(storedDisplayValue),
            LocalizationService.DisplayOptionalValue(defaultDisplayValue),
            LocalizationService.GetFormattedString("SettingsInteractivePostStopSuspendSoundDetails", LidGuardSupportedSystemSounds.Describe()));

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = LocalizationService.GetFormattedString("SettingsInteractiveInputEnded", settingName);
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        value = valueText.Trim().Equals("off", StringComparison.OrdinalIgnoreCase) ? string.Empty : valueText.Trim();
        return true;
    }

    public static bool TryReadClosedLidPermissionRequestDecisionSetting(
        string settingName,
        ClosedLidPermissionRequestDecision storedValue,
        ClosedLidPermissionRequestDecision defaultValue,
        out ClosedLidPermissionRequestDecision value,
        out string message)
    {
        value = storedValue;
        message = string.Empty;
        WriteInteractiveSettingPrompt(
            settingName,
            LocalizationService.DisplayClosedLidPermissionRequestDecision(storedValue),
            LocalizationService.DisplayClosedLidPermissionRequestDecision(defaultValue),
            LocalizationService.GetString("SettingsInteractiveClosedLidPermissionRequestDecisionDetails", "candidates: Deny, Allow"));

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = LocalizationService.GetFormattedString("SettingsInteractiveInputEnded", settingName);
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        return LidGuardSettingsValueParser.TryParseClosedLidPermissionRequestDecision(valueText, out value, out message);
    }

    public static bool TryReadUserInterfaceCultureSetting(
        string settingName,
        string storedValue,
        string defaultValue,
        out string value,
        out string message)
    {
        value = storedValue;
        message = string.Empty;
        WriteInteractiveSettingPrompt(
            settingName,
            storedValue,
            defaultValue,
            LocalizationService.GetString("SettingsInteractiveUserInterfaceCultureDetails", "auto, en, ko, or a culture name such as ko-KR"));

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = LocalizationService.GetFormattedString("SettingsInteractiveInputEnded", settingName);
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        return UserInterfaceCultureConfiguration.TryNormalizeConfiguredValue(valueText, out value, out message);
    }

    private static void WriteInteractiveSettingPrompt(
        string settingName,
        string storedValueText,
        string defaultValueText,
        string additionalDetails = "")
    {
        var prompt = string.IsNullOrWhiteSpace(additionalDetails)
            ? LocalizationService.GetFormattedString("SettingsInteractivePrompt", settingName, storedValueText, defaultValueText)
            : LocalizationService.GetFormattedString("SettingsInteractivePromptWithDetails", settingName, storedValueText, defaultValueText, additionalDetails);
        Console.Write(prompt);
    }
}
