using System.Text.Json;
using LidGuardLib.Commons.Power;
using LidGuardLib.Commons.Settings;

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

        try
        {
            var content = File.ReadAllText(settingsFilePath);
            var loadedSettings = JsonSerializer.Deserialize(content, LidGuardSettingsFileJsonSerializerContext.Default.LidGuardSettings);
            settings = MigrateLoadedSettings(content, loadedSettings);
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

    public static bool TrySave(LidGuardSettings settings, out string message)
    {
        var settingsFilePath = GetDefaultSettingsFilePath();
        var normalizedSettings = LidGuardSettings.Normalize(settings);

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

    private static LidGuardSettings MigrateLoadedSettings(string content, LidGuardSettings loadedSettings)
    {
        var normalizedSettings = LidGuardSettings.Normalize(loadedSettings);
        if (HasPowerRequestProperty(content, nameof(PowerRequestOptions.PreventAwayModeSleep))) return normalizedSettings;

        var powerRequest = normalizedSettings.PowerRequest ?? PowerRequestOptions.Default;
        return new LidGuardSettings
        {
            PowerRequest = new PowerRequestOptions
            {
                PreventSystemSleep = powerRequest.PreventSystemSleep,
                PreventAwayModeSleep = PowerRequestOptions.Default.PreventAwayModeSleep,
                PreventDisplaySleep = powerRequest.PreventDisplaySleep,
                Reason = powerRequest.Reason
            },
            ChangeLidAction = normalizedSettings.ChangeLidAction,
            SuspendMode = normalizedSettings.SuspendMode,
            PostStopSuspendDelaySeconds = normalizedSettings.PostStopSuspendDelaySeconds,
            PostStopSuspendSound = normalizedSettings.PostStopSuspendSound,
            PreSuspendWebhookUrl = normalizedSettings.PreSuspendWebhookUrl,
            ClosedLidPermissionRequestDecision = normalizedSettings.ClosedLidPermissionRequestDecision,
            WatchParentProcess = normalizedSettings.WatchParentProcess
        };
    }

    private static bool HasPowerRequestProperty(string content, string propertyName)
    {
        using var jsonDocument = JsonDocument.Parse(content);
        if (!jsonDocument.RootElement.TryGetProperty(nameof(LidGuardSettings.PowerRequest), out var powerRequestElement)) return false;

        foreach (var jsonProperty in powerRequestElement.EnumerateObject())
        {
            if (jsonProperty.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }
}

