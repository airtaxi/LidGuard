using System.Diagnostics;
using System.Globalization;
using System.Security;
using System.Text;
using LidGuard.Settings;

namespace LidGuard.Localization;

internal static class LidGuardCulture
{
    public const string UserInterfaceCultureEnvironmentVariableName = "LIDGUARD_UI_CULTURE";
    private static readonly CultureInfo s_processDefaultUserInterfaceCulture;

    // Keep this as an explicit static constructor so auto culture is captured before stored settings are applied.
    static LidGuardCulture() => s_processDefaultUserInterfaceCulture = CultureInfo.CurrentUICulture;

    public static void ApplyEffectiveCultureFromEnvironmentOrSettings()
    {
        var environmentCultureName = Environment.GetEnvironmentVariable(UserInterfaceCultureEnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(environmentCultureName))
        {
            if (TryApplyConfiguredCulture(environmentCultureName, out var environmentMessage)) return;

            Console.Error.WriteLine(CultureInvalidEnvironmentWarning(environmentCultureName, environmentMessage));
        }

        if (!LidGuardSettingsStore.TryLoadExistingOrDefault(out var settings, out _, out var settingsMessage))
        {
            if (!string.IsNullOrWhiteSpace(settingsMessage)) Console.Error.WriteLine(LocalizationService.GetFormattedString("CultureSettingsLoadWarning", settingsMessage));
            ConfigureWindowsUnicodeConsoleEncoding(CultureInfo.CurrentUICulture);
            return;
        }

        var normalizedSettings = LidGuardSettings.Normalize(settings);
        if (UserInterfaceCultureConfiguration.IsAutomatic(normalizedSettings.UserInterfaceCulture))
        {
            ApplyCultureInfo(s_processDefaultUserInterfaceCulture);
            return;
        }

        if (TryApplyConfiguredCulture(normalizedSettings.UserInterfaceCulture, out var message)) return;

        Console.Error.WriteLine(LocalizationService.GetFormattedString("CultureInvalidUserInterfaceCultureWarning", normalizedSettings.UserInterfaceCulture, message));
        ConfigureWindowsUnicodeConsoleEncoding(CultureInfo.CurrentUICulture);
    }

    public static void ApplyEffectiveCulture(LidGuardSettings settings)
    {
        var environmentCultureName = Environment.GetEnvironmentVariable(UserInterfaceCultureEnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(environmentCultureName))
        {
            if (!TryApplyConfiguredCulture(environmentCultureName, out var message))
            {
                Console.Error.WriteLine(CultureInvalidEnvironmentWarning(environmentCultureName, message));
                ConfigureWindowsUnicodeConsoleEncoding(CultureInfo.CurrentUICulture);
            }

            return;
        }

        var normalizedSettings = LidGuardSettings.Normalize(settings);
        if (UserInterfaceCultureConfiguration.IsAutomatic(normalizedSettings.UserInterfaceCulture))
        {
            ApplyCultureInfo(s_processDefaultUserInterfaceCulture);
            return;
        }

        if (TryApplyConfiguredCulture(normalizedSettings.UserInterfaceCulture, out var settingsMessage)) return;

        Console.Error.WriteLine(LocalizationService.GetFormattedString("CultureInvalidUserInterfaceCultureWarning", normalizedSettings.UserInterfaceCulture, settingsMessage));
        ConfigureWindowsUnicodeConsoleEncoding(CultureInfo.CurrentUICulture);
    }

    public static void ConfigureChildProcessCulture(ProcessStartInfo processStartInfo)
    {
        ArgumentNullException.ThrowIfNull(processStartInfo);

        var cultureName = CultureInfo.CurrentUICulture.Name;
        if (string.IsNullOrWhiteSpace(cultureName)) return;

        if (processStartInfo.UseShellExecute)
        {
            Environment.SetEnvironmentVariable(UserInterfaceCultureEnvironmentVariableName, cultureName);
            return;
        }

        processStartInfo.Environment[UserInterfaceCultureEnvironmentVariableName] = cultureName;
    }

    public static string ResolveEffectiveCultureName(LidGuardSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var environmentCultureName = Environment.GetEnvironmentVariable(UserInterfaceCultureEnvironmentVariableName);
        if (TryResolveConcreteCultureName(environmentCultureName, out var environmentEffectiveCultureName)) return environmentEffectiveCultureName;

        var normalizedSettings = LidGuardSettings.Normalize(settings);
        if (!UserInterfaceCultureConfiguration.IsAutomatic(normalizedSettings.UserInterfaceCulture)
            && TryResolveConcreteCultureName(normalizedSettings.UserInterfaceCulture, out var settingsEffectiveCultureName))
        {
            return settingsEffectiveCultureName;
        }

        return ResolveConcreteCultureNameOrEnglish(s_processDefaultUserInterfaceCulture);
    }

    private static bool TryApplyConfiguredCulture(string cultureName, out string message)
    {
        if (!UserInterfaceCultureConfiguration.TryCreateCultureInfo(cultureName, out var cultureInfo, out message)) return false;

        ApplyCultureInfo(cultureInfo);
        return true;
    }

    private static bool TryResolveConcreteCultureName(string cultureName, out string concreteCultureName)
    {
        concreteCultureName = string.Empty;
        if (string.IsNullOrWhiteSpace(cultureName)) return false;
        if (!UserInterfaceCultureConfiguration.TryCreateCultureInfo(cultureName, out var cultureInfo, out _)) return false;

        concreteCultureName = ResolveConcreteCultureNameOrEnglish(cultureInfo);
        return true;
    }

    private static string ResolveConcreteCultureNameOrEnglish(CultureInfo cultureInfo)
    {
        if (cultureInfo is null) return "en";
        return string.IsNullOrWhiteSpace(cultureInfo.Name) ? "en" : cultureInfo.Name;
    }

    private static void ApplyCultureInfo(CultureInfo cultureInfo)
    {
        CultureInfo.CurrentUICulture = cultureInfo;
        CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;
        ConfigureWindowsUnicodeConsoleEncoding(cultureInfo);
    }

    private static void ConfigureWindowsUnicodeConsoleEncoding(CultureInfo cultureInfo)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (IsEnglishCulture(cultureInfo)) return;

        try { Console.OutputEncoding = Encoding.UTF8; }
        catch (IOException) { }
        catch (SecurityException) { }

        try { Console.InputEncoding = Encoding.UTF8; }
        catch (IOException) { }
        catch (SecurityException) { }
    }

    private static bool IsEnglishCulture(CultureInfo cultureInfo)
    {
        if (cultureInfo is null) return true;
        if (string.IsNullOrWhiteSpace(cultureInfo.Name)) return true;
        return cultureInfo.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase);
    }

    private static string CultureInvalidEnvironmentWarning(string cultureName, string detail)
        => LocalizationService.GetFormattedString("CultureInvalidUserInterfaceCultureWarning",
            $"{UserInterfaceCultureEnvironmentVariableName}={cultureName}",
            detail);
}
