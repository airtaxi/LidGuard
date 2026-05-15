using System.Globalization;
using System.Resources;

namespace LidGuard.Notifications.Localization;

internal static class LocalizationService
{
    private static readonly ResourceManager s_resourceManager = new("LidGuard.Notifications.Resources.LidGuardNotificationText", typeof(LocalizationService).Assembly);

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
}
