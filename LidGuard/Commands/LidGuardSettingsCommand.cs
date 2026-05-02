using LidGuard.Control;
using LidGuard.Ipc;
using LidGuard.Runtime;
using LidGuard.Settings;
using LidGuardLib.Commons.Platform;
using LidGuardLib.Commons.Power;
using LidGuardLib.Commons.Settings;
using LidGuardLib.Commons.Services;
using LidGuardLib.Power;

namespace LidGuard.Commands;

internal static class LidGuardSettingsCommand
{
    public static async Task<int> SendSettingsAsync(IReadOnlyDictionary<string, string> options, ILidGuardRuntimePlatform runtimePlatform)
    {
        if (!LidGuardSettingsStore.TryLoadOrCreate(out var currentSettings, out var loadMessage))
        {
            Console.Error.WriteLine(loadMessage);
            return 1;
        }

        var postStopSuspendSoundPlayerResult = runtimePlatform.CreatePostStopSuspendSoundPlayer();
        if (!postStopSuspendSoundPlayerResult.Succeeded)
        {
            Console.Error.WriteLine(postStopSuspendSoundPlayerResult.Message);
            return 1;
        }

        var settings = LidGuardSettings.Default;
        var settingsMessage = string.Empty;
        var isInteractiveSettings = options.Count == 0;
        var settingsCreated = isInteractiveSettings
            ? TryCreateInteractiveSettings(currentSettings, out settings, out settingsMessage)
            : TryCreateSettings(options, currentSettings, out settings, out settingsMessage);

        if (!settingsCreated)
        {
            Console.Error.WriteLine(settingsMessage);
            return 1;
        }

        if (!PostStopSuspendSoundConfiguration.TryNormalize(
            settings,
            postStopSuspendSoundPlayerResult.Value,
            out settings,
            out settingsMessage))
        {
            Console.Error.WriteLine(settingsMessage);
            return 1;
        }

        if (!LidGuardSettingsStore.TrySave(settings, out var saveMessage))
        {
            Console.Error.WriteLine(saveMessage);
            return 1;
        }

        var request = new LidGuardPipeRequest
        {
            Command = LidGuardPipeCommands.Settings,
            HasSettings = true,
            Settings = settings
        };

        var response = await new LidGuardRuntimeClient().SendAsync(request, false);
        Console.WriteLine($"Settings file: {LidGuardSettingsStore.GetDefaultSettingsFilePath()}");
        LidGuardCommandConsole.WriteSettings(settings);
        if (isInteractiveSettings)
        {
            Console.WriteLine($"To change Reason, run: {LidGuardCommandConsole.GetCommandDisplayName()} settings --power-request-reason <text>");
            Console.WriteLine($"To change Pre-suspend webhook URL, run: {LidGuardCommandConsole.GetCommandDisplayName()} settings --pre-suspend-webhook-url <http-or-https-url>");
            Console.WriteLine($"To remove Pre-suspend webhook URL, run: {LidGuardCommandConsole.GetCommandDisplayName()} {LidGuardPipeCommands.RemovePreSuspendWebhook}");
        }

        if (response.Succeeded)
        {
            Console.WriteLine("Runtime settings updated.");
            return 0;
        }

        if (response.RuntimeUnavailable)
        {
            Console.WriteLine("Runtime is not running; saved settings will be used on the next start.");
            return 0;
        }

        Console.Error.WriteLine(response.Message);
        return 1;
    }

    public static async Task<int> SendRemovePreSuspendWebhookAsync(
        IReadOnlyDictionary<string, string> options,
        ILidGuardRuntimePlatform runtimePlatform)
    {
        if (options.Count > 0)
        {
            Console.Error.WriteLine($"{LidGuardPipeCommands.RemovePreSuspendWebhook} does not accept options.");
            return 1;
        }

        if (!LidGuardSettingsStore.TryLoadOrCreate(out var currentSettings, out var loadMessage))
        {
            Console.Error.WriteLine(loadMessage);
            return 1;
        }

        var normalizedCurrentSettings = LidGuardSettings.Normalize(currentSettings);
        if (string.IsNullOrWhiteSpace(normalizedCurrentSettings.PreSuspendWebhookUrl))
        {
            Console.WriteLine("No pre-suspend webhook URL is configured.");
            return 0;
        }

        var postStopSuspendSoundPlayerResult = runtimePlatform.CreatePostStopSuspendSoundPlayer();
        if (!postStopSuspendSoundPlayerResult.Succeeded)
        {
            Console.Error.WriteLine(postStopSuspendSoundPlayerResult.Message);
            return 1;
        }

        var controlService = new LidGuardControlService(postStopSuspendSoundPlayerResult.Value);
        var updateResult = await controlService.UpdateSettingsAsync(
            new LidGuardSettingsPatch { PreSuspendWebhookUrl = string.Empty });
        if (!updateResult.Succeeded)
        {
            Console.Error.WriteLine(updateResult.Message);
            return 1;
        }

        var outcome = updateResult.Value;
        Console.WriteLine($"Settings file: {LidGuardSettingsStore.GetDefaultSettingsFilePath()}");
        LidGuardCommandConsole.WriteSettings(outcome.UpdatedStoredSettings);
        Console.WriteLine("Pre-suspend webhook URL removed.");

        if (outcome.Snapshot.RuntimeReachable)
        {
            Console.WriteLine("Runtime settings updated.");
            return 0;
        }

        if (outcome.Snapshot.RuntimeUnavailable)
        {
            Console.WriteLine("Runtime is not running; saved settings will be used on the next start.");
            return 0;
        }

        Console.Error.WriteLine(outcome.Snapshot.RuntimeMessage);
        return 1;
    }

    public static int PreviewCurrentSound(IReadOnlyDictionary<string, string> options, ILidGuardRuntimePlatform runtimePlatform)
    {
        if (options.Count > 0)
        {
            Console.Error.WriteLine($"{LidGuardPipeCommands.PreviewCurrentSound} does not accept options.");
            return 1;
        }

        var postStopSuspendSoundPlayerResult = runtimePlatform.CreatePostStopSuspendSoundPlayer();
        if (!postStopSuspendSoundPlayerResult.Succeeded)
        {
            Console.Error.WriteLine(postStopSuspendSoundPlayerResult.Message);
            return 1;
        }

        if (!LidGuardSettingsStore.TryLoadExistingOrDefault(out var storedSettings, out _, out var settingsMessage))
        {
            Console.Error.WriteLine(settingsMessage);
            return 1;
        }

        var normalizedStoredSettings = LidGuardSettings.Normalize(storedSettings);
        var normalizeResult = postStopSuspendSoundPlayerResult.Value.NormalizeConfiguration(normalizedStoredSettings.PostStopSuspendSound);
        if (!normalizeResult.Succeeded)
        {
            Console.Error.WriteLine(normalizeResult.Message);
            return 1;
        }

        if (string.IsNullOrWhiteSpace(normalizeResult.Value))
        {
            WriteCurrentSoundConfigurationGuide();
            return 0;
        }

        var systemAudioVolumeControllerResult = runtimePlatform.CreateSystemAudioVolumeController();
        if (!systemAudioVolumeControllerResult.Succeeded)
        {
            Console.Error.WriteLine(systemAudioVolumeControllerResult.Message);
            return 1;
        }

        return PreviewPostStopSuspendSound(
            normalizeResult.Value,
            normalizedStoredSettings.PostStopSuspendSoundVolumeOverridePercent,
            postStopSuspendSoundPlayerResult.Value,
            systemAudioVolumeControllerResult.Value,
            $"Played current post-stop suspend sound: {PostStopSuspendSoundConfiguration.GetDisplayValue(normalizeResult.Value)}");
    }

    public static int PreviewSystemSound(IReadOnlyDictionary<string, string> options, ILidGuardRuntimePlatform runtimePlatform)
    {
        var postStopSuspendSoundPlayerResult = runtimePlatform.CreatePostStopSuspendSoundPlayer();
        if (!postStopSuspendSoundPlayerResult.Succeeded)
        {
            Console.Error.WriteLine(postStopSuspendSoundPlayerResult.Message);
            return 1;
        }

        var systemAudioVolumeControllerResult = runtimePlatform.CreateSystemAudioVolumeController();
        if (!systemAudioVolumeControllerResult.Succeeded)
        {
            Console.Error.WriteLine(systemAudioVolumeControllerResult.Message);
            return 1;
        }

        if (!LidGuardSettingsStore.TryLoadExistingOrDefault(out var storedSettings, out _, out var settingsMessage))
        {
            Console.Error.WriteLine(settingsMessage);
            return 1;
        }

        var systemSoundName = CommandOptionReader.GetOption(options, "name", "system-sound");
        if (string.IsNullOrWhiteSpace(systemSoundName))
        {
            Console.Error.WriteLine($"A system sound name is required. Supported values: {LidGuardSupportedSystemSounds.Describe()}");
            return 1;
        }

        var normalizedSystemSoundName = systemSoundName.Trim();
        if (!LidGuardSupportedSystemSounds.Names.Any(
            supportedSystemSoundName => supportedSystemSoundName.Equals(normalizedSystemSoundName, StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine($"Unsupported system sound name: {normalizedSystemSoundName}");
            Console.Error.WriteLine($"Supported values: {LidGuardSupportedSystemSounds.Describe()}");
            return 1;
        }

        var normalizedStoredSettings = LidGuardSettings.Normalize(storedSettings);
        return PreviewPostStopSuspendSound(
            normalizedSystemSoundName,
            normalizedStoredSettings.PostStopSuspendSoundVolumeOverridePercent,
            postStopSuspendSoundPlayerResult.Value,
            systemAudioVolumeControllerResult.Value,
            $"Played system sound: {normalizedSystemSoundName}");
    }

    private static int PreviewPostStopSuspendSound(
        string postStopSuspendSound,
        int? postStopSuspendSoundVolumeOverridePercent,
        IPostStopSuspendSoundPlayer postStopSuspendSoundPlayer,
        ISystemAudioVolumeController systemAudioVolumeController,
        string successMessage)
    {
        var playbackCoordinator = new PostStopSuspendSoundPlaybackCoordinator(
            postStopSuspendSoundPlayer,
            systemAudioVolumeController);
        var playbackResult = playbackCoordinator.PlayAsync(
                postStopSuspendSound,
                postStopSuspendSoundVolumeOverridePercent,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        foreach (var volumeWarningResult in playbackResult.VolumeWarningResults)
        {
            Console.Error.WriteLine($"Warning: {volumeWarningResult.Message}");
        }

        if (!playbackResult.PlaybackResult.Succeeded)
        {
            Console.Error.WriteLine(playbackResult.PlaybackResult.Message);
            return 1;
        }

        Console.WriteLine(successMessage);
        Console.WriteLine($"Volume override setting: {PostStopSuspendSoundConfiguration.GetVolumeOverrideDisplayValue(postStopSuspendSoundVolumeOverridePercent)}");
        return 0;
    }

    private static void WriteCurrentSoundConfigurationGuide()
    {
        var commandDisplayName = LidGuardCommandConsole.GetCommandDisplayName();
        Console.WriteLine("No post-stop suspend sound is configured.");
        Console.WriteLine($"Configure one with: {commandDisplayName} settings --post-stop-suspend-sound Asterisk");
        Console.WriteLine($"Supported system sounds: {LidGuardSupportedSystemSounds.Describe()}");
        Console.WriteLine("A playable .wav file path is also supported.");
        Console.WriteLine($"Optional volume override: {commandDisplayName} settings --post-stop-suspend-sound-volume-override-percent 75");
    }

    private static bool TryCreateSettings(
        IReadOnlyDictionary<string, string> options,
        LidGuardSettings currentSettings,
        out LidGuardSettings settings,
        out string message)
    {
        settings = LidGuardSettings.Normalize(currentSettings);
        if (!CommandOptionReader.TryParseBooleanOption(options, false, out var resetSettings, out message, "reset", "default", "defaults")) return false;

        var baseSettings = resetSettings ? LidGuardSettings.HeadlessRuntimeDefault : settings;
        var basePowerRequest = baseSettings.PowerRequest ?? PowerRequestOptions.Default;
        settings = baseSettings;
        message = string.Empty;

        if (!CommandOptionReader.TryParseBooleanOption(options, basePowerRequest.PreventSystemSleep, out var preventSystemSleep, out message, "prevent-system-sleep", "system-required")) return false;
        if (!CommandOptionReader.TryParseBooleanOption(options, basePowerRequest.PreventAwayModeSleep, out var preventAwayModeSleep, out message, "prevent-away-mode-sleep", "away-mode-required")) return false;
        if (!CommandOptionReader.TryParseBooleanOption(options, basePowerRequest.PreventDisplaySleep, out var preventDisplaySleep, out message, "prevent-display-sleep", "display-required")) return false;
        if (!CommandOptionReader.TryParseBooleanOption(options, baseSettings.ChangeLidAction, out var changeLidAction, out message, "change-lid-action", "lid-action")) return false;
        if (!CommandOptionReader.TryParseBooleanOption(options, baseSettings.WatchParentProcess, out var watchParentProcess, out message, "watch-parent-process", "watch-parent")) return false;
        if (!TryParseSessionTimeoutMinutesOption(
            options,
            baseSettings.SessionTimeoutMinutes,
            out var sessionTimeoutMinutes,
            out message))
            return false;
        if (!TryParseServerRuntimeCleanupDelayMinutesOption(
            options,
            baseSettings.ServerRuntimeCleanupDelayMinutes,
            out var serverRuntimeCleanupDelayMinutes,
            out message))
            return false;
        if (!CommandOptionReader.TryParseBooleanOption(
            options,
            baseSettings.EmergencyHibernationOnHighTemperature,
            out var emergencyHibernationOnHighTemperature,
            out message,
            "emergency-hibernation-on-high-temperature"))
            return false;
        if (!TryParseEmergencyHibernationTemperatureModeOption(
            options,
            baseSettings.EmergencyHibernationTemperatureMode,
            out var emergencyHibernationTemperatureMode,
            out message))
            return false;
        if (!TryParseEmergencyHibernationTemperatureCelsiusOption(
            options,
            baseSettings.EmergencyHibernationTemperatureCelsius,
            out var emergencyHibernationTemperatureCelsius,
            out message))
            return false;
        if (!TryParseSuspendModeOption(options, baseSettings.SuspendMode, out var suspendMode, out message)) return false;
        if (!TryParsePostStopSuspendDelaySecondsOption(options, baseSettings.PostStopSuspendDelaySeconds, out var postStopSuspendDelaySeconds, out message)) return false;
        if (!TryParsePostStopSuspendSoundVolumeOverridePercentOption(
            options,
            baseSettings.PostStopSuspendSoundVolumeOverridePercent,
            out var postStopSuspendSoundVolumeOverridePercent,
            out message))
            return false;
        if (!TryParseSuspendHistoryEntryCountOption(
            options,
            baseSettings.SuspendHistoryEntryCount,
            out var suspendHistoryEntryCount,
            out message))
            return false;
        var postStopSuspendSound = baseSettings.PostStopSuspendSound;
        if (CommandOptionReader.TryGetOption(options, out var postStopSuspendSoundText, "post-stop-suspend-sound")) postStopSuspendSound = postStopSuspendSoundText;
        if (!TryParsePreSuspendWebhookUrlOption(options, baseSettings.PreSuspendWebhookUrl, out var preSuspendWebhookUrl, out message)) return false;
        if (!TryParseClosedLidPermissionRequestDecisionOption(options, baseSettings.ClosedLidPermissionRequestDecision, out var closedLidPermissionRequestDecision, out message)) return false;

        var reason = CommandOptionReader.GetOption(options, "power-request-reason", "reason");
        if (string.IsNullOrWhiteSpace(reason)) reason = basePowerRequest.Reason;

        settings = new LidGuardSettings
        {
            PowerRequest = new PowerRequestOptions
            {
                PreventSystemSleep = preventSystemSleep,
                PreventAwayModeSleep = preventAwayModeSleep,
                PreventDisplaySleep = preventDisplaySleep,
                Reason = reason
            },
            ChangeLidAction = changeLidAction,
            SuspendMode = suspendMode,
            PostStopSuspendDelaySeconds = postStopSuspendDelaySeconds,
            PostStopSuspendSound = postStopSuspendSound,
            PostStopSuspendSoundVolumeOverridePercent = postStopSuspendSoundVolumeOverridePercent,
            SuspendHistoryEntryCount = suspendHistoryEntryCount,
            PreSuspendWebhookUrl = preSuspendWebhookUrl,
            ClosedLidPermissionRequestDecision = closedLidPermissionRequestDecision,
            WatchParentProcess = watchParentProcess,
            SessionTimeoutMinutes = sessionTimeoutMinutes,
            ServerRuntimeCleanupDelayMinutes = serverRuntimeCleanupDelayMinutes,
            EmergencyHibernationOnHighTemperature = emergencyHibernationOnHighTemperature,
            EmergencyHibernationTemperatureMode = emergencyHibernationTemperatureMode,
            EmergencyHibernationTemperatureCelsius = emergencyHibernationTemperatureCelsius
        };

        return true;
    }

    private static bool TryCreateInteractiveSettings(LidGuardSettings currentSettings, out LidGuardSettings settings, out string message)
    {
        var normalizedStoredSettings = LidGuardSettings.Normalize(currentSettings);
        var storedPowerRequest = normalizedStoredSettings.PowerRequest ?? PowerRequestOptions.Default;
        var defaultSettings = LidGuardSettings.Normalize(LidGuardSettings.HeadlessRuntimeDefault);
        var defaultPowerRequest = defaultSettings.PowerRequest ?? PowerRequestOptions.Default;
        settings = normalizedStoredSettings;
        message = string.Empty;

        if (!TryReadBooleanSetting("Prevent system sleep", storedPowerRequest.PreventSystemSleep, defaultPowerRequest.PreventSystemSleep, out var preventSystemSleep, out message)) return false;
        if (!TryReadBooleanSetting("Prevent away mode sleep", storedPowerRequest.PreventAwayModeSleep, defaultPowerRequest.PreventAwayModeSleep, out var preventAwayModeSleep, out message)) return false;
        if (!TryReadBooleanSetting("Prevent display sleep", storedPowerRequest.PreventDisplaySleep, defaultPowerRequest.PreventDisplaySleep, out var preventDisplaySleep, out message)) return false;
        if (!TryReadBooleanSetting("Change lid action", normalizedStoredSettings.ChangeLidAction, defaultSettings.ChangeLidAction, out var changeLidAction, out message)) return false;
        if (!TryReadBooleanSetting("Watch parent process", normalizedStoredSettings.WatchParentProcess, defaultSettings.WatchParentProcess, out var watchParentProcess, out message)) return false;
        if (!TryReadSessionTimeoutMinutesSetting(
            "Session timeout minutes",
            normalizedStoredSettings.SessionTimeoutMinutes,
            defaultSettings.SessionTimeoutMinutes,
            out var sessionTimeoutMinutes,
            out message))
            return false;
        if (!TryReadServerRuntimeCleanupDelayMinutesSetting(
            "Server runtime cleanup delay minutes",
            normalizedStoredSettings.ServerRuntimeCleanupDelayMinutes,
            defaultSettings.ServerRuntimeCleanupDelayMinutes,
            out var serverRuntimeCleanupDelayMinutes,
            out message))
            return false;
        if (!TryReadBooleanSetting(
            "Emergency hibernation on high temperature",
            normalizedStoredSettings.EmergencyHibernationOnHighTemperature,
            defaultSettings.EmergencyHibernationOnHighTemperature,
            out var emergencyHibernationOnHighTemperature,
            out message))
            return false;
        if (!TryReadEmergencyHibernationTemperatureModeSetting(
            "Emergency hibernation temperature mode",
            normalizedStoredSettings.EmergencyHibernationTemperatureMode,
            defaultSettings.EmergencyHibernationTemperatureMode,
            out var emergencyHibernationTemperatureMode,
            out message))
            return false;
        if (!TryReadEmergencyHibernationTemperatureCelsiusSetting(
            "Emergency hibernation temperature Celsius",
            normalizedStoredSettings.EmergencyHibernationTemperatureCelsius,
            defaultSettings.EmergencyHibernationTemperatureCelsius,
            out var emergencyHibernationTemperatureCelsius,
            out message))
            return false;
        if (!TryReadSuspendModeSetting("Suspend mode", normalizedStoredSettings.SuspendMode, defaultSettings.SuspendMode, out var suspendMode, out message)) return false;
        if (!TryReadNonNegativeIntegerSetting(
            "Post-stop suspend delay seconds",
            normalizedStoredSettings.PostStopSuspendDelaySeconds,
            defaultSettings.PostStopSuspendDelaySeconds,
            out var postStopSuspendDelaySeconds,
            out message))
            return false;
        if (!TryReadPostStopSuspendSoundSetting(
            "Post-stop suspend sound",
            normalizedStoredSettings.PostStopSuspendSound,
            defaultSettings.PostStopSuspendSound,
            out var postStopSuspendSound,
            out message))
            return false;
        if (!TryReadPostStopSuspendSoundVolumeOverridePercentSetting(
            "Post-stop suspend sound volume override percent",
            normalizedStoredSettings.PostStopSuspendSoundVolumeOverridePercent,
            defaultSettings.PostStopSuspendSoundVolumeOverridePercent,
            out var postStopSuspendSoundVolumeOverridePercent,
            out message))
            return false;
        if (!TryReadSuspendHistoryEntryCountSetting(
            "Suspend history entry count",
            normalizedStoredSettings.SuspendHistoryEntryCount,
            defaultSettings.SuspendHistoryEntryCount,
            out var suspendHistoryEntryCount,
            out message))
            return false;
        if (!TryReadClosedLidPermissionRequestDecisionSetting(
            "Closed lid permission request decision",
            normalizedStoredSettings.ClosedLidPermissionRequestDecision,
            defaultSettings.ClosedLidPermissionRequestDecision,
            out var closedLidPermissionRequestDecision,
            out message))
            return false;

        settings = new LidGuardSettings
        {
            PowerRequest = new PowerRequestOptions
            {
                PreventSystemSleep = preventSystemSleep,
                PreventAwayModeSleep = preventAwayModeSleep,
                PreventDisplaySleep = preventDisplaySleep,
                Reason = storedPowerRequest.Reason
            },
            ChangeLidAction = changeLidAction,
            SuspendMode = suspendMode,
            PostStopSuspendDelaySeconds = postStopSuspendDelaySeconds,
            PostStopSuspendSound = postStopSuspendSound,
            PostStopSuspendSoundVolumeOverridePercent = postStopSuspendSoundVolumeOverridePercent,
            SuspendHistoryEntryCount = suspendHistoryEntryCount,
            PreSuspendWebhookUrl = normalizedStoredSettings.PreSuspendWebhookUrl,
            ClosedLidPermissionRequestDecision = closedLidPermissionRequestDecision,
            WatchParentProcess = watchParentProcess,
            SessionTimeoutMinutes = sessionTimeoutMinutes,
            ServerRuntimeCleanupDelayMinutes = serverRuntimeCleanupDelayMinutes,
            EmergencyHibernationOnHighTemperature = emergencyHibernationOnHighTemperature,
            EmergencyHibernationTemperatureMode = emergencyHibernationTemperatureMode,
            EmergencyHibernationTemperatureCelsius = emergencyHibernationTemperatureCelsius
        };

        return true;
    }

    private static bool TryReadBooleanSetting(string settingName, bool storedValue, bool defaultValue, out bool value, out string message)
    {
        value = storedValue;
        message = string.Empty;
        WriteInteractiveSettingPrompt(settingName, storedValue.ToString(), defaultValue.ToString());

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = $"Input ended before {settingName} was entered.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        if (TryParseInteractiveBoolean(valueText.Trim(), out value)) return true;

        message = $"{settingName} must be true or false.";
        return false;
    }

    private static bool TryReadSuspendModeSetting(
        string settingName,
        SystemSuspendMode storedValue,
        SystemSuspendMode defaultValue,
        out SystemSuspendMode value,
        out string message)
    {
        value = storedValue;
        message = string.Empty;
        WriteInteractiveSettingPrompt(settingName, storedValue.ToString(), defaultValue.ToString(), "candidates: Sleep, Hibernate");

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = $"Input ended before {settingName} was entered.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;

        var normalizedValueText = valueText.Trim();
        value = normalizedValueText.ToLowerInvariant() switch
        {
            "sleep" => SystemSuspendMode.Sleep,
            "hibernate" => SystemSuspendMode.Hibernate,
            _ => storedValue
        };

        if (normalizedValueText.Equals("sleep", StringComparison.OrdinalIgnoreCase)) return true;
        if (normalizedValueText.Equals("hibernate", StringComparison.OrdinalIgnoreCase)) return true;

        message = $"{settingName} must be sleep or hibernate.";
        return false;
    }

    private static bool TryReadNonNegativeIntegerSetting(
        string settingName,
        int storedValue,
        int defaultValue,
        out int value,
        out string message)
    {
        value = storedValue;
        message = string.Empty;
        WriteInteractiveSettingPrompt(settingName, storedValue.ToString(), defaultValue.ToString(), "0 = immediate");

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = $"Input ended before {settingName} was entered.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        if (int.TryParse(valueText.Trim(), out value) && value >= 0) return true;

        message = $"{settingName} must be a non-negative integer.";
        return false;
    }

    private static bool TryReadSessionTimeoutMinutesSetting(
        string settingName,
        int? storedValue,
        int? defaultValue,
        out int? value,
        out string message)
    {
        value = storedValue;
        message = string.Empty;
        WriteInteractiveSettingPrompt(
            settingName,
            SessionTimeoutConfiguration.GetDisplayValue(storedValue),
            SessionTimeoutConfiguration.GetDisplayValue(defaultValue),
            $"minimum: {LidGuardSettings.MinimumSessionTimeoutMinutes}, off to disable");

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = $"Input ended before {settingName} was entered.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        if (valueText.Trim().Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            value = null;
            return true;
        }

        if (int.TryParse(valueText.Trim(), out var parsedValue)
            && LidGuardSettings.IsValidSessionTimeoutMinutes(parsedValue))
        {
            value = parsedValue;
            return true;
        }

        message = $"{settingName} must be off or an integer of at least {LidGuardSettings.MinimumSessionTimeoutMinutes}.";
        return false;
    }

    private static bool TryReadServerRuntimeCleanupDelayMinutesSetting(
        string settingName,
        int? storedValue,
        int? defaultValue,
        out int? value,
        out string message)
    {
        value = storedValue;
        message = string.Empty;
        WriteInteractiveSettingPrompt(
            settingName,
            ServerRuntimeCleanupConfiguration.GetDisplayValue(storedValue),
            ServerRuntimeCleanupConfiguration.GetDisplayValue(defaultValue),
            $"minimum: {LidGuardSettings.MinimumServerRuntimeCleanupDelayMinutes}, off to exit immediately");

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = $"Input ended before {settingName} was entered.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        if (valueText.Trim().Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            value = null;
            return true;
        }

        if (int.TryParse(valueText.Trim(), out var parsedValue)
            && LidGuardSettings.IsValidServerRuntimeCleanupDelayMinutes(parsedValue))
        {
            value = parsedValue;
            return true;
        }

        message = $"{settingName} must be off or an integer of at least {LidGuardSettings.MinimumServerRuntimeCleanupDelayMinutes}.";
        return false;
    }

    private static bool TryReadPostStopSuspendSoundVolumeOverridePercentSetting(
        string settingName,
        int? storedValue,
        int? defaultValue,
        out int? value,
        out string message)
    {
        value = storedValue;
        message = string.Empty;
        WriteInteractiveSettingPrompt(
            settingName,
            PostStopSuspendSoundConfiguration.GetVolumeOverrideDisplayValue(storedValue),
            PostStopSuspendSoundConfiguration.GetVolumeOverrideDisplayValue(defaultValue),
            $"range: {LidGuardSettings.MinimumPostStopSuspendSoundVolumeOverridePercent}-{LidGuardSettings.MaximumPostStopSuspendSoundVolumeOverridePercent}, off to disable");

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = $"Input ended before {settingName} was entered.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        if (valueText.Trim().Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            value = null;
            return true;
        }

        if (int.TryParse(valueText.Trim(), out var parsedValue)
            && LidGuardSettings.IsValidPostStopSuspendSoundVolumeOverridePercent(parsedValue))
        {
            value = parsedValue;
            return true;
        }

        message =
            $"{settingName} must be off or an integer from {LidGuardSettings.MinimumPostStopSuspendSoundVolumeOverridePercent} through {LidGuardSettings.MaximumPostStopSuspendSoundVolumeOverridePercent}.";
        return false;
    }

    private static bool TryReadSuspendHistoryEntryCountSetting(
        string settingName,
        int? storedValue,
        int? defaultValue,
        out int? value,
        out string message)
    {
        value = storedValue;
        message = string.Empty;
        WriteInteractiveSettingPrompt(
            settingName,
            SuspendHistoryConfiguration.GetDisplayValue(storedValue),
            SuspendHistoryConfiguration.GetDisplayValue(defaultValue),
            $"minimum: {LidGuardSettings.MinimumSuspendHistoryEntryCount}, off to disable");

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = $"Input ended before {settingName} was entered.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        if (valueText.Trim().Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            value = null;
            return true;
        }

        if (int.TryParse(valueText.Trim(), out var parsedValue)
            && LidGuardSettings.IsValidSuspendHistoryEntryCount(parsedValue))
        {
            value = parsedValue;
            return true;
        }

        message = $"{settingName} must be off or an integer of at least {LidGuardSettings.MinimumSuspendHistoryEntryCount}.";
        return false;
    }

    private static bool TryReadEmergencyHibernationTemperatureCelsiusSetting(
        string settingName,
        int storedValue,
        int defaultValue,
        out int value,
        out string message)
    {
        value = storedValue;
        message = string.Empty;
        WriteInteractiveSettingPrompt(
            settingName,
            storedValue.ToString(),
            defaultValue.ToString(),
            $"range: {LidGuardSettings.MinimumEmergencyHibernationTemperatureCelsius}-{LidGuardSettings.MaximumEmergencyHibernationTemperatureCelsius}");

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = $"Input ended before {settingName} was entered.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        if (int.TryParse(valueText.Trim(), out value)
            && value >= LidGuardSettings.MinimumEmergencyHibernationTemperatureCelsius
            && value <= LidGuardSettings.MaximumEmergencyHibernationTemperatureCelsius)
            return true;

        message =
            $"{settingName} must be an integer from {LidGuardSettings.MinimumEmergencyHibernationTemperatureCelsius} through {LidGuardSettings.MaximumEmergencyHibernationTemperatureCelsius}.";
        return false;
    }

    private static bool TryReadEmergencyHibernationTemperatureModeSetting(
        string settingName,
        EmergencyHibernationTemperatureMode storedValue,
        EmergencyHibernationTemperatureMode defaultValue,
        out EmergencyHibernationTemperatureMode value,
        out string message)
    {
        value = storedValue;
        message = string.Empty;
        WriteInteractiveSettingPrompt(settingName, storedValue.ToString(), defaultValue.ToString(), "candidates: Low, Average, High");

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = $"Input ended before {settingName} was entered.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        if (TryParseEmergencyHibernationTemperatureMode(valueText, out value)) return true;

        message = $"{settingName} must be low, average, or high.";
        return false;
    }

    private static bool TryReadPostStopSuspendSoundSetting(
        string settingName,
        string storedValue,
        string defaultValue,
        out string value,
        out string message)
    {
        value = storedValue;
        message = string.Empty;
        var storedDisplayValue = PostStopSuspendSoundConfiguration.GetDisplayValue(storedValue);
        var defaultDisplayValue = PostStopSuspendSoundConfiguration.GetDisplayValue(defaultValue);
        WriteInteractiveSettingPrompt(
            settingName,
            storedDisplayValue,
            defaultDisplayValue,
            $"use off to disable, SystemSounds: {LidGuardSupportedSystemSounds.Describe()}, or a .wav path");

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = $"Input ended before {settingName} was entered.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        value = valueText.Trim().Equals("off", StringComparison.OrdinalIgnoreCase) ? string.Empty : valueText.Trim();
        return true;
    }

    private static bool TryParsePreSuspendWebhookUrlOption(
        IReadOnlyDictionary<string, string> options,
        string defaultValue,
        out string preSuspendWebhookUrl,
        out string message)
    {
        preSuspendWebhookUrl = defaultValue;
        message = string.Empty;
        if (!CommandOptionReader.TryGetOption(options, out var preSuspendWebhookUrlText, "pre-suspend-webhook-url", "suspend-webhook-url")) return true;

        if (string.IsNullOrWhiteSpace(preSuspendWebhookUrlText) || preSuspendWebhookUrlText.Trim().Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            message = $"Use {LidGuardCommandConsole.GetCommandDisplayName()} {LidGuardPipeCommands.RemovePreSuspendWebhook} to remove the pre-suspend webhook URL.";
            return false;
        }

        return PreSuspendWebhookConfiguration.TryNormalizeConfiguredValue(
            preSuspendWebhookUrlText,
            out preSuspendWebhookUrl,
            out message);
    }

    private static bool TryReadClosedLidPermissionRequestDecisionSetting(
        string settingName,
        ClosedLidPermissionRequestDecision storedValue,
        ClosedLidPermissionRequestDecision defaultValue,
        out ClosedLidPermissionRequestDecision value,
        out string message)
    {
        value = storedValue;
        message = string.Empty;
        WriteInteractiveSettingPrompt(settingName, storedValue.ToString(), defaultValue.ToString(), "candidates: Deny, Allow");

        var valueText = Console.ReadLine();
        if (valueText is null)
        {
            message = $"Input ended before {settingName} was entered.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueText)) return true;
        return TryParseClosedLidPermissionRequestDecision(valueText, out value, out message);
    }

    private static void WriteInteractiveSettingPrompt(
        string settingName,
        string storedValueText,
        string defaultValueText,
        string additionalDetails = "")
    {
        var prompt = $"{settingName} (stored: {storedValueText}, default: {defaultValueText}";
        if (!string.IsNullOrWhiteSpace(additionalDetails)) prompt = $"{prompt}, {additionalDetails}";
        prompt = $"{prompt}, press Enter to keep stored): ";
        Console.Write(prompt);
    }

    private static bool TryParseInteractiveBoolean(string valueText, out bool value)
    {
        value = false;
        if (valueText.Equals(bool.TrueString, StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (valueText.Equals(bool.FalseString, StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        return false;
    }

    private static bool TryParseClosedLidPermissionRequestDecisionOption(
        IReadOnlyDictionary<string, string> options,
        ClosedLidPermissionRequestDecision defaultValue,
        out ClosedLidPermissionRequestDecision closedLidPermissionRequestDecision,
        out string message)
    {
        closedLidPermissionRequestDecision = defaultValue;
        message = string.Empty;
        if (!CommandOptionReader.TryGetOption(options, out var permissionRequestDecisionText, "closed-lid-permission-request-decision", "permission-request-decision-when-lid-closed")) return true;
        return TryParseClosedLidPermissionRequestDecision(permissionRequestDecisionText, out closedLidPermissionRequestDecision, out message);
    }

    private static bool TryParseClosedLidPermissionRequestDecision(
        string permissionRequestDecisionText,
        out ClosedLidPermissionRequestDecision closedLidPermissionRequestDecision,
        out string message)
    {
        closedLidPermissionRequestDecision = ClosedLidPermissionRequestDecision.Deny;
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(permissionRequestDecisionText))
        {
            message = "Closed lid permission request decision must be deny or allow.";
            return false;
        }

        switch (permissionRequestDecisionText.Trim().ToLowerInvariant())
        {
            case "allow":
                closedLidPermissionRequestDecision = ClosedLidPermissionRequestDecision.Allow;
                return true;
            case "deny":
                closedLidPermissionRequestDecision = ClosedLidPermissionRequestDecision.Deny;
                return true;
            default:
                message = "Closed lid permission request decision must be deny or allow.";
                return false;
        }
    }

    private static bool TryParseSuspendModeOption(
        IReadOnlyDictionary<string, string> options,
        SystemSuspendMode defaultValue,
        out SystemSuspendMode suspendMode,
        out string message)
    {
        suspendMode = defaultValue;
        message = string.Empty;
        if (!CommandOptionReader.TryGetOption(options, out var suspendModeText, "suspend-mode")) return true;

        var normalizedSuspendModeText = suspendModeText.Trim();
        suspendMode = normalizedSuspendModeText.ToLowerInvariant() switch
        {
            "sleep" => SystemSuspendMode.Sleep,
            "hibernate" => SystemSuspendMode.Hibernate,
            _ => defaultValue
        };

        if (suspendMode == defaultValue && !normalizedSuspendModeText.Equals(defaultValue.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            message = "The suspend-mode option must be sleep or hibernate.";
            return false;
        }

        return true;
    }

    private static bool TryParsePostStopSuspendDelaySecondsOption(
        IReadOnlyDictionary<string, string> options,
        int defaultValue,
        out int postStopSuspendDelaySeconds,
        out string message)
    {
        postStopSuspendDelaySeconds = defaultValue;
        message = string.Empty;
        if (!CommandOptionReader.TryGetOption(options, out var postStopSuspendDelaySecondsText, "post-stop-suspend-delay-seconds")) return true;
        if (int.TryParse(postStopSuspendDelaySecondsText.Trim(), out postStopSuspendDelaySeconds) && postStopSuspendDelaySeconds >= 0) return true;

        message = "The post-stop-suspend-delay-seconds option must be a non-negative integer.";
        return false;
    }

    private static bool TryParsePostStopSuspendSoundVolumeOverridePercentOption(
        IReadOnlyDictionary<string, string> options,
        int? defaultValue,
        out int? postStopSuspendSoundVolumeOverridePercent,
        out string message)
    {
        postStopSuspendSoundVolumeOverridePercent = defaultValue;
        message = string.Empty;
        if (!CommandOptionReader.TryGetOption(
            options,
            out var postStopSuspendSoundVolumeOverridePercentText,
            "post-stop-suspend-sound-volume-override-percent"))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(postStopSuspendSoundVolumeOverridePercentText)
            || postStopSuspendSoundVolumeOverridePercentText.Trim().Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            postStopSuspendSoundVolumeOverridePercent = null;
            return true;
        }

        if (int.TryParse(postStopSuspendSoundVolumeOverridePercentText.Trim(), out var parsedValue)
            && LidGuardSettings.IsValidPostStopSuspendSoundVolumeOverridePercent(parsedValue))
        {
            postStopSuspendSoundVolumeOverridePercent = parsedValue;
            return true;
        }

        message =
            $"The post-stop-suspend-sound-volume-override-percent option must be off or an integer from {LidGuardSettings.MinimumPostStopSuspendSoundVolumeOverridePercent} through {LidGuardSettings.MaximumPostStopSuspendSoundVolumeOverridePercent}.";
        return false;
    }

    private static bool TryParseSuspendHistoryEntryCountOption(
        IReadOnlyDictionary<string, string> options,
        int? defaultValue,
        out int? suspendHistoryEntryCount,
        out string message)
    {
        suspendHistoryEntryCount = defaultValue;
        message = string.Empty;
        if (!CommandOptionReader.TryGetOption(
            options,
            out var suspendHistoryEntryCountText,
            "suspend-history-count",
            "suspend-history-entry-count"))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(suspendHistoryEntryCountText)
            || suspendHistoryEntryCountText.Trim().Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            suspendHistoryEntryCount = null;
            return true;
        }

        if (int.TryParse(suspendHistoryEntryCountText.Trim(), out var parsedValue)
            && LidGuardSettings.IsValidSuspendHistoryEntryCount(parsedValue))
        {
            suspendHistoryEntryCount = parsedValue;
            return true;
        }

        message = $"The suspend-history-count option must be off or an integer of at least {LidGuardSettings.MinimumSuspendHistoryEntryCount}.";
        return false;
    }

    private static bool TryParseSessionTimeoutMinutesOption(
        IReadOnlyDictionary<string, string> options,
        int? defaultValue,
        out int? sessionTimeoutMinutes,
        out string message)
    {
        sessionTimeoutMinutes = defaultValue;
        message = string.Empty;
        if (!CommandOptionReader.TryGetOption(options, out var sessionTimeoutMinutesText, "session-timeout-minutes")) return true;

        if (string.IsNullOrWhiteSpace(sessionTimeoutMinutesText)
            || sessionTimeoutMinutesText.Trim().Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            sessionTimeoutMinutes = null;
            return true;
        }

        if (int.TryParse(sessionTimeoutMinutesText.Trim(), out var parsedValue)
            && LidGuardSettings.IsValidSessionTimeoutMinutes(parsedValue))
        {
            sessionTimeoutMinutes = parsedValue;
            return true;
        }

        message = $"The session-timeout-minutes option must be off or an integer of at least {LidGuardSettings.MinimumSessionTimeoutMinutes}.";
        return false;
    }

    private static bool TryParseServerRuntimeCleanupDelayMinutesOption(
        IReadOnlyDictionary<string, string> options,
        int? defaultValue,
        out int? serverRuntimeCleanupDelayMinutes,
        out string message)
    {
        serverRuntimeCleanupDelayMinutes = defaultValue;
        message = string.Empty;
        if (!CommandOptionReader.TryGetOption(
            options,
            out var serverRuntimeCleanupDelayMinutesText,
            "server-runtime-cleanup-delay-minutes"))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(serverRuntimeCleanupDelayMinutesText)
            || serverRuntimeCleanupDelayMinutesText.Trim().Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            serverRuntimeCleanupDelayMinutes = null;
            return true;
        }

        if (int.TryParse(serverRuntimeCleanupDelayMinutesText.Trim(), out var parsedValue)
            && LidGuardSettings.IsValidServerRuntimeCleanupDelayMinutes(parsedValue))
        {
            serverRuntimeCleanupDelayMinutes = parsedValue;
            return true;
        }

        message = $"The server-runtime-cleanup-delay-minutes option must be off or an integer of at least {LidGuardSettings.MinimumServerRuntimeCleanupDelayMinutes}.";
        return false;
    }

    private static bool TryParseEmergencyHibernationTemperatureCelsiusOption(
        IReadOnlyDictionary<string, string> options,
        int defaultValue,
        out int emergencyHibernationTemperatureCelsius,
        out string message)
    {
        emergencyHibernationTemperatureCelsius = defaultValue;
        message = string.Empty;
        if (!CommandOptionReader.TryGetOption(options, out var emergencyHibernationTemperatureCelsiusText, "emergency-hibernation-temperature-celsius")) return true;
        if (int.TryParse(emergencyHibernationTemperatureCelsiusText.Trim(), out emergencyHibernationTemperatureCelsius)
            && emergencyHibernationTemperatureCelsius >= LidGuardSettings.MinimumEmergencyHibernationTemperatureCelsius
            && emergencyHibernationTemperatureCelsius <= LidGuardSettings.MaximumEmergencyHibernationTemperatureCelsius)
            return true;

        message =
            $"The emergency-hibernation-temperature-celsius option must be an integer from {LidGuardSettings.MinimumEmergencyHibernationTemperatureCelsius} through {LidGuardSettings.MaximumEmergencyHibernationTemperatureCelsius}.";
        return false;
    }

    private static bool TryParseEmergencyHibernationTemperatureModeOption(
        IReadOnlyDictionary<string, string> options,
        EmergencyHibernationTemperatureMode defaultValue,
        out EmergencyHibernationTemperatureMode emergencyHibernationTemperatureMode,
        out string message)
    {
        emergencyHibernationTemperatureMode = defaultValue;
        message = string.Empty;
        if (!CommandOptionReader.TryGetOption(options, out var emergencyHibernationTemperatureModeText, "emergency-hibernation-temperature-mode")) return true;
        if (TryParseEmergencyHibernationTemperatureMode(emergencyHibernationTemperatureModeText, out emergencyHibernationTemperatureMode)) return true;

        message = "The emergency-hibernation-temperature-mode option must be low, average, or high.";
        return false;
    }

    public static bool TryParseEmergencyHibernationTemperatureMode(
        string emergencyHibernationTemperatureModeText,
        out EmergencyHibernationTemperatureMode emergencyHibernationTemperatureMode)
    {
        emergencyHibernationTemperatureMode = EmergencyHibernationTemperatureMode.Average;
        if (string.IsNullOrWhiteSpace(emergencyHibernationTemperatureModeText)) return false;

        switch (emergencyHibernationTemperatureModeText.Trim().ToLowerInvariant())
        {
            case "low":
                emergencyHibernationTemperatureMode = EmergencyHibernationTemperatureMode.Low;
                return true;
            case "average":
                emergencyHibernationTemperatureMode = EmergencyHibernationTemperatureMode.Average;
                return true;
            case "high":
                emergencyHibernationTemperatureMode = EmergencyHibernationTemperatureMode.High;
                return true;
            default:
                return false;
        }
    }
}
