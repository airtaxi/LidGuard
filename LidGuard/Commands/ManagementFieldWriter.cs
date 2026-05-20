using LidGuard.Hooks;
using LidGuard.Localization;

namespace LidGuard.Commands;

internal static class ManagementFieldWriter
{
    public static void WriteField(string labelResourceName, string fallbackLabel, object value)
    {
        var displayValue = value switch
        {
            bool booleanValue => LocalizationService.DisplayBoolean(booleanValue),
            HookInstallationStatus status => DisplayHookInstallationStatus(status),
            _ => LocalizationService.DisplayOptionalValue(value?.ToString() ?? string.Empty)
        };
        Console.WriteLine(LocalizationService.GetFormattedString("ManagementField", LocalizationService.GetString(labelResourceName, fallbackLabel), displayValue));
    }

    public static void WriteField(string labelResourceName, string fallbackLabel, HookInstallationStatus status)
        => WriteField(labelResourceName, fallbackLabel, (object)status);

    private static string DisplayHookInstallationStatus(HookInstallationStatus status)
        => LocalizationService.GetString($"DisplayHookInstallationStatus{status}", status.ToString());
}
