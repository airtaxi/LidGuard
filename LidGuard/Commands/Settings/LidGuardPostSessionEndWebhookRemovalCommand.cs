using LidGuard.Control;
using LidGuard.Ipc;
using LidGuard.Localization;
using LidGuard.Settings;
using LidGuard.Platform;

namespace LidGuard.Commands;

internal static class LidGuardPostSessionEndWebhookRemovalCommand
{
    public static async Task<int> SendRemovePostSessionEndWebhookAsync(
        IReadOnlyDictionary<string, string> options,
        ILidGuardRuntimePlatform runtimePlatform)
    {
        if (options.Count > 0)
        {
            Console.Error.WriteLine(LocalizationService.GetFormattedString("CommandDoesNotAcceptOptions", LidGuardPipeCommands.RemovePostSessionEndWebhook));
            return 1;
        }

        if (!LidGuardSettingsStore.TryLoadOrCreate(out var currentSettings, out var loadMessage))
        {
            Console.Error.WriteLine(loadMessage);
            return 1;
        }

        var normalizedCurrentSettings = LidGuardSettings.Normalize(currentSettings);
        if (string.IsNullOrWhiteSpace(normalizedCurrentSettings.PostSessionEndWebhookUrl))
        {
            Console.WriteLine(LocalizationService.GetString("SettingsNoPostSessionEndWebhookConfigured"));
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
            new LidGuardSettingsPatch { PostSessionEndWebhookUrl = string.Empty });
        if (!updateResult.Succeeded)
        {
            Console.Error.WriteLine(updateResult.Message);
            return 1;
        }

        var outcome = updateResult.Value;
        Console.WriteLine(LocalizationService.GetFormattedString("ConsoleSettingsFile", LidGuardSettingsStore.GetDefaultSettingsFilePath()));
        LidGuardCommandConsole.WriteSettings(outcome.UpdatedStoredSettings);
        Console.WriteLine(LocalizationService.GetString("SettingsPostSessionEndWebhookUrlRemoved"));

        if (outcome.Snapshot.RuntimeReachable)
        {
            Console.WriteLine(LocalizationService.GetString("SettingsRuntimeUpdated"));
            return 0;
        }

        if (outcome.Snapshot.RuntimeUnavailable)
        {
            Console.WriteLine(LocalizationService.GetString("SettingsRuntimeNotRunningSaved"));
            return 0;
        }

        Console.Error.WriteLine(LidGuardRuntimeResponseLocalizer.Localize(
            outcome.Snapshot.RuntimeMessageCode,
            outcome.Snapshot.RuntimeMessageArguments,
            outcome.Snapshot.RuntimeMessage));
        return 1;
    }
}
