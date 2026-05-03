using LidGuard.Ipc;
using LidGuard.Localization;
using LidGuard.Settings;
using LidGuard.Platform;

namespace LidGuard.Commands;

internal static class LidGuardSettingsUpdateCommand
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
            ? LidGuardSettingsInteractiveFactory.TryCreateSettings(currentSettings, out settings, out settingsMessage)
            : LidGuardSettingsCommandLineFactory.TryCreateSettings(options, currentSettings, out settings, out settingsMessage);

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

        var shouldRefreshManagedHookStatusMessages = ShouldRefreshManagedHookStatusMessages(options, currentSettings, settings);
        LidGuardCulture.ApplyEffectiveCulture(settings);
        var managedHookStatusMessageRefreshResult = shouldRefreshManagedHookStatusMessages
            ? ManagedHookStatusMessageRefresh.RefreshInstalledManagedHooks()
            : null;

        var request = new LidGuardPipeRequest
        {
            Command = LidGuardPipeCommands.Settings,
            HasSettings = true,
            Settings = settings
        };

        var response = await new LidGuardRuntimeClient().SendAsync(request, false);
        Console.WriteLine(LidGuardText.ConsoleSettingsFile(LidGuardSettingsStore.GetDefaultSettingsFilePath()));
        LidGuardCommandConsole.WriteSettings(settings);
        if (managedHookStatusMessageRefreshResult is not null) WriteManagedHookStatusMessageRefreshResult(managedHookStatusMessageRefreshResult);
        if (isInteractiveSettings)
        {
            var commandDisplayName = LidGuardCommandConsole.GetCommandDisplayName();
            Console.WriteLine(LidGuardText.SettingsInteractiveGuidanceChangeReason(commandDisplayName));
            Console.WriteLine(LidGuardText.SettingsInteractiveGuidanceChangePreSuspendWebhook(commandDisplayName));
            Console.WriteLine(LidGuardText.SettingsInteractiveGuidanceRemovePreSuspendWebhook(commandDisplayName, LidGuardPipeCommands.RemovePreSuspendWebhook));
            Console.WriteLine(LidGuardText.SettingsInteractiveGuidanceChangePostSessionEndWebhook(commandDisplayName));
            Console.WriteLine(LidGuardText.SettingsInteractiveGuidanceRemovePostSessionEndWebhook(commandDisplayName, LidGuardPipeCommands.RemovePostSessionEndWebhook));
        }

        if (response.Succeeded)
        {
            Console.WriteLine(LidGuardText.SettingsRuntimeUpdated);
            return 0;
        }

        if (response.RuntimeUnavailable)
        {
            Console.WriteLine(LidGuardText.SettingsRuntimeNotRunningSaved);
            return 0;
        }

        Console.Error.WriteLine(LidGuardRuntimeResponseLocalizer.Localize(response));
        return 1;
    }

    private static bool ShouldRefreshManagedHookStatusMessages(
        IReadOnlyDictionary<string, string> options,
        LidGuardSettings currentSettings,
        LidGuardSettings settings)
    {
        if (CommandOptionReader.TryGetOption(options, out _, "ui-culture", "user-interface-culture")) return true;

        var normalizedCurrentSettings = LidGuardSettings.Normalize(currentSettings);
        var normalizedSettings = LidGuardSettings.Normalize(settings);
        return !normalizedCurrentSettings.UserInterfaceCulture.Equals(normalizedSettings.UserInterfaceCulture, StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteManagedHookStatusMessageRefreshResult(ManagedHookStatusMessageRefreshResult result)
    {
        var message = result.ChangedProviderNames.Count > 0
            ? LidGuardText.SettingsManagedHookStatusMessageRefreshChanged(string.Join(", ", result.ChangedProviderNames))
            : LidGuardText.SettingsManagedHookStatusMessageRefreshUnchanged;

        Console.WriteLine(message);
        foreach (var warningMessage in result.WarningMessages) Console.WriteLine(LidGuardText.TextWarning(LidGuardText.SettingsManagedHookStatusMessageRefreshFailed(warningMessage)));
    }
}
