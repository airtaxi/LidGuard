using System.Globalization;
using System.Resources;
using LidGuard.Power;
using LidGuard.Sessions;
using LidGuard.Settings;

namespace LidGuard.Localization;

internal static class LocalizationService
{
    private static readonly ResourceManager s_resourceManager = new("LidGuard.Resources.LidGuardText", typeof(LocalizationService).Assembly);

    public static string GetString(string resourceName) => GetString(resourceName, resourceName);

    public static string GetString(string resourceName, string fallbackValue)
    {
        var localizedString = s_resourceManager.GetString(resourceName, CultureInfo.CurrentUICulture);
        return string.IsNullOrWhiteSpace(localizedString) ? fallbackValue : localizedString;
    }

    public static string GetFormattedString(string resourceName, params object[] arguments)
        => string.Format(CultureInfo.CurrentCulture, GetString(resourceName), arguments);

    public static string GetFormattedStringWithFallback(string resourceName, string fallbackValue, params object[] arguments)
        => string.Format(CultureInfo.CurrentCulture, GetString(resourceName, fallbackValue), arguments);

    public static string DisplayBoolean(bool value)
        => value ? GetString("TextDisplayBooleanTrue") : GetString("TextDisplayBooleanFalse");

    public static string DisplayClosedLidPermissionRequestDecision(ClosedLidPermissionRequestDecision value)
        => GetString($"DisplayClosedLidPermissionRequestDecision{value}", value.ToString());

    public static string DisplayEmergencyHibernationTemperatureMode(EmergencyHibernationTemperatureMode value)
        => GetString($"DisplayEmergencyHibernationTemperatureMode{value}", value.ToString());

    public static string DisplayLidSwitchState(LidSwitchState value)
        => GetString($"DisplayLidSwitchState{value}", value.ToString());

    public static string DisplayMinuteCount(int? value)
        => value is null ? GetString("TextDisplayOff") : GetFormattedString("DisplayMinuteCount", value.Value);

    public static string DisplayOptionalValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return GetString("TextDisplayNone");
        if (value.Equals("<none>", StringComparison.OrdinalIgnoreCase)) return GetString("TextDisplayNone");
        if (value.Equals("off", StringComparison.OrdinalIgnoreCase)) return GetString("TextDisplayOff");
        return value;
    }

    public static string DisplaySessionSoftLockState(LidGuardSessionSoftLockState value)
        => GetString($"DisplaySessionSoftLockState{value}", value.ToString());

    public static string DisplaySuspendMode(SystemSuspendMode value)
        => GetString($"DisplaySuspendMode{value}", value.ToString());

    public static string DisplaySuspendHistoryEntryCount(int? value)
        => value is null ? GetString("TextDisplayOff") : GetFormattedString("DisplaySuspendHistoryEntryCount", value.Value);
}
