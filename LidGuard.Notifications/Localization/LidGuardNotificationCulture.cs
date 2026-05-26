using System.Globalization;
using LidGuard.Notifications.Configuration;
using Microsoft.AspNetCore.Localization;

namespace LidGuard.Notifications.Localization;

internal static class LidGuardNotificationCulture
{
    public const string UserInterfaceCultureEnvironmentVariableName = "LIDGUARD_UI_CULTURE";
    private static readonly CultureInfo s_processDefaultUserInterfaceCulture;
    private static readonly CultureInfo[] s_supportedUserInterfaceCultures =
    [
        CultureInfo.GetCultureInfo("en"),
        CultureInfo.GetCultureInfo("ko"),
        CultureInfo.GetCultureInfo("ja"),
        CultureInfo.GetCultureInfo("zh-Hans"),
        CultureInfo.GetCultureInfo("zh-Hant")
    ];

    // Keep this as an explicit static constructor so auto culture is captured before stored options are applied.
    static LidGuardNotificationCulture() => s_processDefaultUserInterfaceCulture = CultureInfo.CurrentUICulture;

    public static void ApplyDefaultCultureFromEnvironmentOrOptions(LidGuardNotificationsOptions options)
    {
        var cultureInfo = TryResolveEnvironmentCulture(out var environmentCultureInfo) ? environmentCultureInfo : ResolveConfiguredOrProcessCultureInfo(options);
        ApplyCultureInfo(cultureInfo);
    }

    public static RequestLocalizationOptions CreateRequestLocalizationOptions(LidGuardNotificationsOptions options)
    {
        var hasEnvironmentCulture = TryResolveEnvironmentCulture(out var environmentCultureInfo);
        var defaultCultureInfo = hasEnvironmentCulture ? environmentCultureInfo : ResolveConfiguredOrProcessCultureInfo(options);
        var supportedCultureInfos = CreateSupportedCultureInfos(defaultCultureInfo);
        var requestLocalizationOptions = new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture(defaultCultureInfo),
            SupportedCultures = supportedCultureInfos,
            SupportedUICultures = supportedCultureInfos,
            FallBackToParentCultures = true,
            FallBackToParentUICultures = true
        };

        if (hasEnvironmentCulture) requestLocalizationOptions.RequestCultureProviders = [new CustomRequestCultureProvider(_ => Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(environmentCultureInfo.Name)))];

        return requestLocalizationOptions;
    }

    public static bool TryCreateSelectableCultureInfo(string cultureName, out CultureInfo cultureInfo)
    {
        cultureInfo = CultureInfo.InvariantCulture;
        if (!NotificationUserInterfaceCultureConfiguration.TryCreateCultureInfo(cultureName, out var selectedCultureInfo, out _)) return false;
        return TryResolveSelectableCultureInfo(selectedCultureInfo, out cultureInfo);
    }

    public static string ResolveSelectableCultureName(CultureInfo cultureInfo) => TryResolveSelectableCultureInfo(cultureInfo, out var selectableCultureInfo) ? selectableCultureInfo.Name : "en";

    private static bool TryResolveEnvironmentCulture(out CultureInfo cultureInfo)
    {
        var environmentCultureName = Environment.GetEnvironmentVariable(UserInterfaceCultureEnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(environmentCultureName)) return NotificationUserInterfaceCultureConfiguration.TryCreateCultureInfo(environmentCultureName, out cultureInfo, out _);

        cultureInfo = CultureInfo.InvariantCulture;
        return false;
    }

    private static bool TryResolveConfiguredCulture(LidGuardNotificationsOptions options, out CultureInfo cultureInfo)
    {
        var configuredCultureName = NotificationUserInterfaceCultureConfiguration.NormalizeStoredValue(options.UserInterfaceCulture);
        if (NotificationUserInterfaceCultureConfiguration.IsAutomatic(configuredCultureName))
        {
            cultureInfo = CultureInfo.InvariantCulture;
            return false;
        }

        return NotificationUserInterfaceCultureConfiguration.TryCreateCultureInfo(configuredCultureName, out cultureInfo, out _);
    }

    private static CultureInfo ResolveConfiguredOrProcessCultureInfo(LidGuardNotificationsOptions options)
    {
        if (TryResolveConfiguredCulture(options, out var configuredCultureInfo)) return configuredCultureInfo;

        return s_processDefaultUserInterfaceCulture;
    }

    private static List<CultureInfo> CreateSupportedCultureInfos(CultureInfo defaultCultureInfo)
    {
        var supportedCultureInfos = new List<CultureInfo>(s_supportedUserInterfaceCultures);
        if (!supportedCultureInfos.Any(cultureInfo => cultureInfo.Name.Equals(defaultCultureInfo.Name, StringComparison.OrdinalIgnoreCase))) supportedCultureInfos.Add(defaultCultureInfo);

        return supportedCultureInfos;
    }

    private static void ApplyCultureInfo(CultureInfo cultureInfo)
    {
        CultureInfo.CurrentCulture = cultureInfo;
        CultureInfo.CurrentUICulture = cultureInfo;
        CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
        CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;
    }

    private static bool TryResolveSelectableCultureInfo(CultureInfo cultureInfo, out CultureInfo selectableCultureInfo)
    {
        selectableCultureInfo = CultureInfo.InvariantCulture;
        if (cultureInfo is null) return false;

        var currentCultureInfo = cultureInfo;
        while (!string.IsNullOrWhiteSpace(currentCultureInfo.Name))
        {
            for (var index = 0; index < s_supportedUserInterfaceCultures.Length; index++)
            {
                var supportedCultureInfo = s_supportedUserInterfaceCultures[index];
                if (supportedCultureInfo.Name.Equals(currentCultureInfo.Name, StringComparison.OrdinalIgnoreCase))
                {
                    selectableCultureInfo = supportedCultureInfo;
                    return true;
                }
            }

            currentCultureInfo = currentCultureInfo.Parent;
        }

        var twoLetterIsoLanguageName = cultureInfo.TwoLetterISOLanguageName;
        if (string.IsNullOrWhiteSpace(twoLetterIsoLanguageName)) return false;

        for (var index = 0; index < s_supportedUserInterfaceCultures.Length; index++)
        {
            var supportedCultureInfo = s_supportedUserInterfaceCultures[index];
            if (supportedCultureInfo.Name.Equals(twoLetterIsoLanguageName, StringComparison.OrdinalIgnoreCase))
            {
                selectableCultureInfo = supportedCultureInfo;
                return true;
            }
        }

        return false;
    }
}
