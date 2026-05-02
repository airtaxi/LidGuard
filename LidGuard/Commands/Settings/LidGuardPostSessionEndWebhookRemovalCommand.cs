using LidGuard.Control;
using LidGuard.Ipc;
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
            Console.Error.WriteLine($"{LidGuardPipeCommands.RemovePostSessionEndWebhook} does not accept options.");
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
            Console.WriteLine("No post-session-end webhook URL is configured.");
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
        Console.WriteLine($"Settings file: {LidGuardSettingsStore.GetDefaultSettingsFilePath()}");
        LidGuardCommandConsole.WriteSettings(outcome.UpdatedStoredSettings);
        Console.WriteLine("Post-session-end webhook URL removed.");

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
}
