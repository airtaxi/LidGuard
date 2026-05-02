using LidGuard.Settings;
using LidGuard.Power;

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
            message = $"Input ended before {settingName} was entered.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        if (LidGuardSettingsValueParser.TryParseInteractiveBoolean(valueText.Trim(), out value)) return true;

        message = $"{settingName} must be true or false.";
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
        WriteInteractiveSettingPrompt(settingName, storedValue.ToString(), defaultValue.ToString(), "candidates: Sleep, Hibernate");

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = $"Input ended before {settingName} was entered.";
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

        message = $"{settingName} must be sleep or hibernate.";
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
        WriteInteractiveSettingPrompt(settingName, storedValue.ToString(), defaultValue.ToString(), "0 = immediate");

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = $"Input ended before {settingName} was entered.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        if (int.TryParse(valueText.Trim(), out value) && value >= 0) return true;

        message = $"{settingName} must be a non-negative integer.";
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
            SessionTimeoutConfiguration.GetDisplayValue(storedValue),
            SessionTimeoutConfiguration.GetDisplayValue(defaultValue),
            $"minimum: {LidGuardSettings.MinimumSessionTimeoutMinutes}, off to disable");

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = $"Input ended before {settingName} was entered.";
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

        message = $"{settingName} must be off or an integer of at least {LidGuardSettings.MinimumSessionTimeoutMinutes}.";
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
            ServerRuntimeCleanupConfiguration.GetDisplayValue(storedValue),
            ServerRuntimeCleanupConfiguration.GetDisplayValue(defaultValue),
            $"minimum: {LidGuardSettings.MinimumServerRuntimeCleanupDelayMinutes}, off to exit immediately");

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = $"Input ended before {settingName} was entered.";
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

        message = $"{settingName} must be off or an integer of at least {LidGuardSettings.MinimumServerRuntimeCleanupDelayMinutes}.";
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
            PostStopSuspendSoundConfiguration.GetVolumeOverrideDisplayValue(storedValue),
            PostStopSuspendSoundConfiguration.GetVolumeOverrideDisplayValue(defaultValue),
            $"range: {LidGuardSettings.MinimumPostStopSuspendSoundVolumeOverridePercent}-{LidGuardSettings.MaximumPostStopSuspendSoundVolumeOverridePercent}, off to disable");

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = $"Input ended before {settingName} was entered.";
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

        message =
            $"{settingName} must be off or an integer from {LidGuardSettings.MinimumPostStopSuspendSoundVolumeOverridePercent} through {LidGuardSettings.MaximumPostStopSuspendSoundVolumeOverridePercent}.";
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
            SuspendHistoryConfiguration.GetDisplayValue(storedValue),
            SuspendHistoryConfiguration.GetDisplayValue(defaultValue),
            $"minimum: {LidGuardSettings.MinimumSuspendHistoryEntryCount}, off to disable");

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = $"Input ended before {settingName} was entered.";
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

        message = $"{settingName} must be off or an integer of at least {LidGuardSettings.MinimumSuspendHistoryEntryCount}.";
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
            $"range: {LidGuardSettings.MinimumEmergencyHibernationTemperatureCelsius}-{LidGuardSettings.MaximumEmergencyHibernationTemperatureCelsius}");

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = $"Input ended before {settingName} was entered.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        if (int.TryParse(valueText.Trim(), out value)
            && value >= LidGuardSettings.MinimumEmergencyHibernationTemperatureCelsius
            && value <= LidGuardSettings.MaximumEmergencyHibernationTemperatureCelsius)
            return true;

        message =
            $"{settingName} must be an integer from {LidGuardSettings.MinimumEmergencyHibernationTemperatureCelsius} through {LidGuardSettings.MaximumEmergencyHibernationTemperatureCelsius}.";
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
        WriteInteractiveSettingPrompt(settingName, storedValue.ToString(), defaultValue.ToString(), "candidates: Low, Average, High");

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = $"Input ended before {settingName} was entered.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        if (LidGuardSettingsValueParser.TryParseEmergencyHibernationTemperatureMode(valueText, out value)) return true;

        message = $"{settingName} must be low, average, or high.";
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
            storedDisplayValue,
            defaultDisplayValue,
            $"use off to disable, SystemSounds: {LidGuardSupportedSystemSounds.Describe()}, or a .wav path");

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = $"Input ended before {settingName} was entered.";
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
        WriteInteractiveSettingPrompt(settingName, storedValue.ToString(), defaultValue.ToString(), "candidates: Deny, Allow");

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = $"Input ended before {settingName} was entered.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        return LidGuardSettingsValueParser.TryParseClosedLidPermissionRequestDecision(valueText, out value, out message);
    }

    private static void WriteInteractiveSettingPrompt(
        string settingName,
        string storedValueText,
        string defaultValueText,
        string additionalDetails = "")
    {
        var prompt = $"{settingName} (stored: {storedValueText}, default: {defaultValueText}";
        if (!string.IsNullOrWhiteSpace(additionalDetails)) prompt = $"{prompt}, {additionalDetails}";
        prompt = $"{prompt}, press Enter to keep stored): ";
        Console.Write(prompt);
    }
}
