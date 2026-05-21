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

        var shouldRefreshManagedHooks = ShouldRefreshManagedHooks(options, currentSettings, settings);
        LidGuardCulture.ApplyEffectiveCulture(settings);
        var managedHookStatusMessageRefreshResult = shouldRefreshManagedHooks
            ? ManagedHookStatusMessageRefresh.RefreshInstalledManagedHooks()
            : null;

        var request = new LidGuardPipeRequest
        {
            Command = LidGuardPipeCommands.Settings,
            HasSettings = true,
            Settings = settings
        };

        var response = await new LidGuardRuntimeClient().SendAsync(request, false);
        Console.WriteLine(LocalizationService.GetFormattedString("ConsoleSettingsFile", LidGuardSettingsStore.GetDefaultSettingsFilePath()));
        LidGuardCommandConsole.WriteSettings(settings);
        if (managedHookStatusMessageRefreshResult is not null) WriteManagedHookRefreshResult(managedHookStatusMessageRefreshResult);
        if (isInteractiveSettings)
        {
            var commandDisplayName = LidGuardCommandConsole.GetCommandDisplayName();
            Console.WriteLine(LocalizationService.GetFormattedString("SettingsInteractiveGuidanceChangeReason", commandDisplayName));
            Console.WriteLine(LocalizationService.GetFormattedString("SettingsInteractiveGuidanceChangePreSuspendWebhook", commandDisplayName));
            Console.WriteLine(LocalizationService.GetFormattedString("SettingsInteractiveGuidanceRemovePreSuspendWebhook", commandDisplayName, LidGuardPipeCommands.RemovePreSuspendWebhook));
            Console.WriteLine(LocalizationService.GetFormattedString("SettingsInteractiveGuidanceChangePostSessionEndWebhook", commandDisplayName));
            Console.WriteLine(LocalizationService.GetFormattedString("SettingsInteractiveGuidanceRemovePostSessionEndWebhook", commandDisplayName, LidGuardPipeCommands.RemovePostSessionEndWebhook));
        }

        if (response.Succeeded)
        {
            Console.WriteLine(LocalizationService.GetString("SettingsRuntimeUpdated"));
            return 0;
        }

        if (response.RuntimeUnavailable)
        {
            Console.WriteLine(LocalizationService.GetString("SettingsRuntimeNotRunningSaved"));
            return 0;
        }

        Console.Error.WriteLine(LidGuardRuntimeResponseLocalizer.Localize(response));
        return 1;
    }

    private static bool ShouldRefreshManagedHooks(
        IReadOnlyDictionary<string, string> options,
        LidGuardSettings currentSettings,
        LidGuardSettings settings)
    {
        if (CommandOptionReader.TryGetOption(options, out _, "ui-culture", "user-interface-culture")) return true;
        if (CommandOptionReader.TryGetOption(options, out _, "post-stop-suspend-delay-seconds")) return true;
        if (CommandOptionReader.TryGetOption(options, out _, "closed-lid-stop-follow-up-webhook-url")) return true;

        var normalizedCurrentSettings = LidGuardSettings.Normalize(currentSettings);
        var normalizedSettings = LidGuardSettings.Normalize(settings);
        if (!normalizedCurrentSettings.UserInterfaceCulture.Equals(normalizedSettings.UserInterfaceCulture, StringComparison.OrdinalIgnoreCase)) return true;
        if (normalizedCurrentSettings.PostStopSuspendDelaySeconds != normalizedSettings.PostStopSuspendDelaySeconds) return true;
        return !normalizedCurrentSettings.ClosedLidStopFollowUpWebhookUrl.Equals(
            normalizedSettings.ClosedLidStopFollowUpWebhookUrl,
            StringComparison.OrdinalIgnoreCase);
    }

    internal static void WriteManagedHookRefreshResult(ManagedHookStatusMessageRefreshResult result)
    {
        var message = result.ChangedProviderNames.Count > 0
            ? LocalizationService.GetFormattedString("SettingsManagedHookStatusMessageRefreshChanged", string.Join(", ", result.ChangedProviderNames))
            : LocalizationService.GetString("SettingsManagedHookStatusMessageRefreshUnchanged");

        Console.WriteLine(message);
        foreach (var warningMessage in result.WarningMessages) Console.WriteLine(LocalizationService.GetFormattedString("TextWarning", LocalizationService.GetFormattedString("SettingsManagedHookStatusMessageRefreshFailed", warningMessage)));
    }
}
