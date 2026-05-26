using System.Globalization;
using System.Resources;

namespace LidGuard.Notifications.Localization;

internal static class LocalizationService
{
    private static readonly ResourceManager s_resourceManager = new("LidGuard.Notifications.Resources.LidGuardNotificationText", typeof(LocalizationService).Assembly);

    public static string GetString(string resourceName)
    {
        var localizedString = s_resourceManager.GetString(resourceName, CultureInfo.CurrentUICulture);
        return string.IsNullOrWhiteSpace(localizedString) ? resourceName : localizedString;
    }

    public static string GetFormattedString(string resourceName, params object[] arguments) => string.Format(CultureInfo.CurrentCulture, GetString(resourceName), arguments);

}
