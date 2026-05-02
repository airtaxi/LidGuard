using System.Text.Json;
using LidGuard.Settings;

namespace LidGuard.Settings;

internal static class LidGuardSettingsStore
{
    private const string ApplicationDataDirectoryName = "LidGuard";
    private const string SettingsFileName = "settings.json";

    public static string GetApplicationDataDirectoryPath()
    {
        var localApplicationDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationDataPath)) localApplicationDataPath = AppContext.BaseDirectory;
        return Path.Combine(localApplicationDataPath, ApplicationDataDirectoryName);
    }

    public static string GetDefaultSettingsFilePath() => Path.Combine(GetApplicationDataDirectoryPath(), SettingsFileName);

    public static bool TryLoadOrCreate(out LidGuardSettings settings, out string message)
    {
        var settingsFilePath = GetDefaultSettingsFilePath();
        if (!File.Exists(settingsFilePath))
        {
            settings = LidGuardSettings.HeadlessRuntimeDefault;
            return TrySave(settings, out message);
        }

        return TryLoad(settingsFilePath, out settings, out message);
    }

    public static bool TryLoadExistingOrDefault(out LidGuardSettings settings, out bool settingsFileExists, out string message)
    {
        var settingsFilePath = GetDefaultSettingsFilePath();
        if (!File.Exists(settingsFilePath))
        {
            settings = LidGuardSettings.HeadlessRuntimeDefault;
            settingsFileExists = false;
            message = string.Empty;
            return true;
        }

        settingsFileExists = true;
        return TryLoad(settingsFilePath, out settings, out message);
    }

    public static bool TrySave(LidGuardSettings settings, out string message)
    {
        var settingsFilePath = GetDefaultSettingsFilePath();
        var normalizedSettings = LidGuardSettings.Normalize(settings);
        if (!PostStopSuspendSoundConfiguration.TryValidateVolumeOverridePercent(
            normalizedSettings.PostStopSuspendSoundVolumeOverridePercent,
            out message))
            return false;
        if (!SuspendHistoryConfiguration.TryValidateEntryCount(normalizedSettings.SuspendHistoryEntryCount, out message)) return false;
        if (!SessionTimeoutConfiguration.TryValidateMinutes(normalizedSettings.SessionTimeoutMinutes, out message)) return false;
        if (!ServerRuntimeCleanupConfiguration.TryValidateDelayMinutes(normalizedSettings.ServerRuntimeCleanupDelayMinutes, out message)) return false;

        try
        {
            var settingsDirectoryPath = Path.GetDirectoryName(settingsFilePath);
            if (!string.IsNullOrWhiteSpace(settingsDirectoryPath)) Directory.CreateDirectory(settingsDirectoryPath);

            var content = JsonSerializer.Serialize(normalizedSettings, LidGuardSettingsFileJsonSerializerContext.Default.LidGuardSettings);
            File.WriteAllText(settingsFilePath, content);
            message = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            message = $"Failed to write LidGuard settings to {settingsFilePath}: {exception.Message}";
            return false;
        }
    }

    private static bool TryLoad(string settingsFilePath, out LidGuardSettings settings, out string message)
    {
        try
        {
            var content = File.ReadAllText(settingsFilePath);
            var loadedSettings = JsonSerializer.Deserialize(content, LidGuardSettingsFileJsonSerializerContext.Default.LidGuardSettings);
            settings = LidGuardSettings.Normalize(loadedSettings);
            message = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            settings = LidGuardSettings.HeadlessRuntimeDefault;
            message = $"Failed to read LidGuard settings from {settingsFilePath}: {exception.Message}";
            return false;
        }
    }

}

