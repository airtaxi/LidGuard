using LidGuard.Results;
using LidGuard.Services;
using LidGuard.Settings;

namespace LidGuard.Runtime;

internal sealed class ConfiguredSoundPlaybackCoordinator(IPostStopSuspendSoundPlayer postStopSuspendSoundPlayer, ISystemAudioVolumeController systemAudioVolumeController)
{
    public async Task<ConfiguredSoundPlaybackResult> PlayAsync(string configuredSound, int? soundVolumeOverridePercent, string soundDescription, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuredSound)) return ConfiguredSoundPlaybackResult.Success();

        if (!PostStopSuspendSoundConfiguration.TryValidateVolumeOverridePercent(soundVolumeOverridePercent, soundDescription, out var validationMessage))
        {
            var validationPlaybackResult = await postStopSuspendSoundPlayer.PlayAsync(configuredSound, cancellationToken);
            return ConfiguredSoundPlaybackResult.FromPlaybackResult(validationPlaybackResult, [LidGuardOperationResult.Failure($"{soundDescription} volume override skipped: {validationMessage}")]);
        }

        if (soundVolumeOverridePercent is null)
        {
            var unmodifiedPlaybackResult = await postStopSuspendSoundPlayer.PlayAsync(configuredSound, cancellationToken);
            return ConfiguredSoundPlaybackResult.FromPlaybackResult(unmodifiedPlaybackResult);
        }

        var warningResults = new List<LidGuardOperationResult>();
        var captureResult = systemAudioVolumeController.CaptureDefaultRenderDeviceState();
        if (!captureResult.Succeeded)
        {
            warningResults.Add(LidGuardOperationResult.Failure($"{soundDescription} volume override skipped because the current system audio volume could not be captured: {captureResult.Message}"));
            var uncapturedPlaybackResult = await postStopSuspendSoundPlayer.PlayAsync(configuredSound, cancellationToken);
            return ConfiguredSoundPlaybackResult.FromPlaybackResult(uncapturedPlaybackResult, [.. warningResults]);
        }

        LidGuardOperationResult playbackResult;
        try
        {
            var applyResult = systemAudioVolumeController.ApplyDefaultRenderDeviceVolumeOverride(soundVolumeOverridePercent.Value);
            if (!applyResult.Succeeded) warningResults.Add(LidGuardOperationResult.Failure($"{soundDescription} volume override could not be applied; playback will continue with the current system audio state: {applyResult.Message}"));

            playbackResult = await postStopSuspendSoundPlayer.PlayAsync(configuredSound, cancellationToken);
        }
        finally
        {
            var restoreResult = systemAudioVolumeController.RestoreDefaultRenderDeviceState(captureResult.Value);
            if (!restoreResult.Succeeded) warningResults.Add(LidGuardOperationResult.Failure($"{soundDescription} volume state could not be restored after playback: {restoreResult.Message}"));
        }

        return ConfiguredSoundPlaybackResult.FromPlaybackResult(playbackResult, [.. warningResults]);
    }
}

internal sealed class ConfiguredSoundPlaybackResult
{
    public LidGuardOperationResult PlaybackResult { get; init; } = LidGuardOperationResult.Success();

    public LidGuardOperationResult[] VolumeWarningResults { get; init; } = [];

    public static ConfiguredSoundPlaybackResult Success() => new();

    public static ConfiguredSoundPlaybackResult FromPlaybackResult(LidGuardOperationResult playbackResult, LidGuardOperationResult[] volumeWarningResults = null) => new()
    {
        PlaybackResult = playbackResult,
        VolumeWarningResults = volumeWarningResults ?? []
    };
}
