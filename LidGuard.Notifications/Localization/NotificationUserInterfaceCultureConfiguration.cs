using System.Globalization;

namespace LidGuard.Notifications.Localization;

internal static class NotificationUserInterfaceCultureConfiguration
{
    public const string AutomaticCultureName = "auto";

    public static bool IsAutomatic(string userInterfaceCulture) => NormalizeStoredValue(userInterfaceCulture).Equals(AutomaticCultureName, StringComparison.OrdinalIgnoreCase);

    public static string NormalizeStoredValue(string userInterfaceCulture) => string.IsNullOrWhiteSpace(userInterfaceCulture) ? AutomaticCultureName : userInterfaceCulture.Trim();

    public static bool TryNormalizeConfiguredValue(string userInterfaceCulture, out string normalizedUserInterfaceCulture, out string message)
    {
        normalizedUserInterfaceCulture = NormalizeStoredValue(userInterfaceCulture);
        message = string.Empty;
        if (IsAutomatic(normalizedUserInterfaceCulture)) return true;
        if (TryCreateCultureInfo(normalizedUserInterfaceCulture, out var cultureInfo, out message))
        {
            normalizedUserInterfaceCulture = cultureInfo.Name;
            return true;
        }

        return false;
    }

    public static bool TryCreateCultureInfo(string userInterfaceCulture, out CultureInfo cultureInfo, out string message)
    {
        cultureInfo = CultureInfo.InvariantCulture;
        message = string.Empty;
        var normalizedUserInterfaceCulture = NormalizeStoredValue(userInterfaceCulture);
        if (IsAutomatic(normalizedUserInterfaceCulture))
        {
            message = LocalizationService.GetString("CultureAutomaticStoredValueCannotBeExplicitCultureName");
            return false;
        }

        try
        {
            cultureInfo = CultureInfo.GetCultureInfo(normalizedUserInterfaceCulture);
            return true;
        }
        catch (CultureNotFoundException exception)
        {
            message = exception.Message;
            return false;
        }
    }
}
