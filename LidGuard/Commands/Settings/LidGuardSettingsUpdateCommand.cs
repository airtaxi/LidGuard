using LidGuard.Ipc;
using LidGuard.Settings;
using LidGuardLib.Commons.Platform;
using LidGuardLib.Commons.Settings;

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
            Console.WriteLine($"To change Post-session-end webhook URL, run: {LidGuardCommandConsole.GetCommandDisplayName()} settings --post-session-end-webhook-url <http-or-https-url>");
            Console.WriteLine($"To remove Post-session-end webhook URL, run: {LidGuardCommandConsole.GetCommandDisplayName()} {LidGuardPipeCommands.RemovePostSessionEndWebhook}");
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
}
