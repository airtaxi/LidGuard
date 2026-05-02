using LidGuard.Ipc;
using LidGuard.Runtime;
using LidGuard.Settings;
using LidGuardLib.Commons.Platform;
using LidGuardLib.Commons.Services;
using LidGuardLib.Commons.Settings;

namespace LidGuard.Commands;

internal static class LidGuardSettingsSoundPreviewCommand
{
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
}
