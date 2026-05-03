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
            message = LidGuardText.SettingsInteractiveInputEnded(settingName);
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        if (LidGuardSettingsValueParser.TryParseInteractiveBoolean(valueText.Trim(), out value)) return true;

        message = LidGuardText.SettingsInteractiveBooleanValidation(settingName);
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
            LidGuardText.DisplaySuspendMode(storedValue),
            LidGuardText.DisplaySuspendMode(defaultValue),
            LidGuardText.GetResourceString("SettingsInteractiveSuspendModeDetails", "candidates: Sleep, Hibernate"));

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = LidGuardText.SettingsInteractiveInputEnded(settingName);
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

        message = LidGuardText.SettingsInteractiveSuspendModeValidation(settingName);
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
        WriteInteractiveSettingPrompt(settingName, storedValue.ToString(), defaultValue.ToString(), LidGuardText.GetResourceString("SettingsInteractiveImmediateDetails", "0 = immediate"));

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = LidGuardText.SettingsInteractiveInputEnded(settingName);
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        if (int.TryParse(valueText.Trim(), out value) && value >= 0) return true;

        message = LidGuardText.SettingsInteractiveNonNegativeIntegerValidation(settingName);
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
            LidGuardText.DisplayMinuteCount(storedValue),
            LidGuardText.DisplayMinuteCount(defaultValue),
            LidGuardText.SettingsInteractiveSessionTimeoutDetails(LidGuardSettings.MinimumSessionTimeoutMinutes));

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = LidGuardText.SettingsInteractiveInputEnded(settingName);
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

        message = LidGuardText.SettingsInteractiveValueOffOrMinimumValidation(settingName, LidGuardSettings.MinimumSessionTimeoutMinutes);
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
            LidGuardText.DisplayMinuteCount(storedValue),
            LidGuardText.DisplayMinuteCount(defaultValue),
            LidGuardText.SettingsInteractiveServerRuntimeCleanupDelayDetails(LidGuardSettings.MinimumServerRuntimeCleanupDelayMinutes));

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = LidGuardText.SettingsInteractiveInputEnded(settingName);
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

        message = LidGuardText.SettingsInteractiveValueOffOrMinimumValidation(settingName, LidGuardSettings.MinimumServerRuntimeCleanupDelayMinutes);
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
            LidGuardText.DisplayOptionalValue(PostStopSuspendSoundConfiguration.GetVolumeOverrideDisplayValue(storedValue)),
            LidGuardText.DisplayOptionalValue(PostStopSuspendSoundConfiguration.GetVolumeOverrideDisplayValue(defaultValue)),
            LidGuardText.SettingsInteractiveVolumeOverrideDetails(
                LidGuardSettings.MinimumPostStopSuspendSoundVolumeOverridePercent,
                LidGuardSettings.MaximumPostStopSuspendSoundVolumeOverridePercent));

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = LidGuardText.SettingsInteractiveInputEnded(settingName);
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

        message = LidGuardText.SettingsInteractiveVolumeOverrideValidation(
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
            LidGuardText.DisplaySuspendHistoryEntryCount(storedValue),
            LidGuardText.DisplaySuspendHistoryEntryCount(defaultValue),
            LidGuardText.SettingsInteractiveSuspendHistoryEntryCountDetails(LidGuardSettings.MinimumSuspendHistoryEntryCount));

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = LidGuardText.SettingsInteractiveInputEnded(settingName);
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

        message = LidGuardText.SettingsInteractiveValueOffOrMinimumValidation(settingName, LidGuardSettings.MinimumSuspendHistoryEntryCount);
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
            LidGuardText.SettingsInteractiveRangeDetails(
                LidGuardSettings.MinimumEmergencyHibernationTemperatureCelsius,
                LidGuardSettings.MaximumEmergencyHibernationTemperatureCelsius));

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = LidGuardText.SettingsInteractiveInputEnded(settingName);
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        if (int.TryParse(valueText.Trim(), out value)
            && value >= LidGuardSettings.MinimumEmergencyHibernationTemperatureCelsius
            && value <= LidGuardSettings.MaximumEmergencyHibernationTemperatureCelsius)
            return true;

        message = LidGuardText.SettingsInteractiveRangeValidation(
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
            LidGuardText.DisplayEmergencyHibernationTemperatureMode(storedValue),
            LidGuardText.DisplayEmergencyHibernationTemperatureMode(defaultValue),
            LidGuardText.GetResourceString("SettingsInteractiveEmergencyHibernationTemperatureModeDetails", "candidates: Low, Average, High"));

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = LidGuardText.SettingsInteractiveInputEnded(settingName);
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        if (LidGuardSettingsValueParser.TryParseEmergencyHibernationTemperatureMode(valueText, out value)) return true;

        message = LidGuardText.SettingsInteractiveEmergencyHibernationTemperatureModeValidation(settingName);
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
            LidGuardText.DisplayOptionalValue(storedDisplayValue),
            LidGuardText.DisplayOptionalValue(defaultDisplayValue),
            LidGuardText.SettingsInteractivePostStopSuspendSoundDetails(LidGuardSupportedSystemSounds.Describe()));

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = LidGuardText.SettingsInteractiveInputEnded(settingName);
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
            LidGuardText.DisplayClosedLidPermissionRequestDecision(storedValue),
            LidGuardText.DisplayClosedLidPermissionRequestDecision(defaultValue),
            LidGuardText.GetResourceString("SettingsInteractiveClosedLidPermissionRequestDecisionDetails", "candidates: Deny, Allow"));

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = LidGuardText.SettingsInteractiveInputEnded(settingName);
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
            LidGuardText.GetResourceString("SettingsInteractiveUserInterfaceCultureDetails", "auto, en, ko, or a culture name such as ko-KR"));

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = LidGuardText.SettingsInteractiveInputEnded(settingName);
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
            ? LidGuardText.SettingsInteractivePrompt(settingName, storedValueText, defaultValueText)
            : LidGuardText.SettingsInteractivePromptWithDetails(settingName, storedValueText, defaultValueText, additionalDetails);
        Console.Write(prompt);
    }
}
