using System.Globalization;
using LidGuard.Localization;

namespace LidGuard.Settings;

internal static class UserInterfaceCultureConfiguration
{
    public const string AutomaticCultureName = "auto";

    public static string GetDisplayValue(string userInterfaceCulture)
        => NormalizeStoredValue(userInterfaceCulture);

    public static bool IsAutomatic(string userInterfaceCulture)
        => NormalizeStoredValue(userInterfaceCulture).Equals(AutomaticCultureName, StringComparison.OrdinalIgnoreCase);

    public static string NormalizeStoredValue(string userInterfaceCulture)
        => string.IsNullOrWhiteSpace(userInterfaceCulture) ? AutomaticCultureName : userInterfaceCulture.Trim();

    public static bool TryNormalizeConfiguredValue(
        string userInterfaceCulture,
        out string normalizedUserInterfaceCulture,
        out string message)
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
            message = LidGuardText.CultureInvalidUserInterfaceCulture(
                normalizedUserInterfaceCulture,
                "auto is a stored settings value and cannot be used as an explicit CultureInfo name.");
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
