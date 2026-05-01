using LidGuardLib.Commons.Results;
using LidGuardLib.Commons.Services;
using LidGuard.Settings;

namespace LidGuard.Runtime;

internal sealed class PostStopSuspendSoundPlaybackCoordinator(
    IPostStopSuspendSoundPlayer postStopSuspendSoundPlayer,
    ISystemAudioVolumeController systemAudioVolumeController)
{
    public async Task<PostStopSuspendSoundPlaybackResult> PlayAsync(
        string postStopSuspendSound,
        int? postStopSuspendSoundVolumeOverridePercent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(postStopSuspendSound)) return PostStopSuspendSoundPlaybackResult.Success();

        if (!PostStopSuspendSoundConfiguration.TryValidateVolumeOverridePercent(postStopSuspendSoundVolumeOverridePercent, out var validationMessage))
        {
            var validationPlaybackResult = await postStopSuspendSoundPlayer.PlayAsync(postStopSuspendSound, cancellationToken);
            return PostStopSuspendSoundPlaybackResult.FromPlaybackResult(
                validationPlaybackResult,
                [LidGuardOperationResult.Failure($"Post-stop suspend sound volume override skipped: {validationMessage}")]);
        }

        if (postStopSuspendSoundVolumeOverridePercent is null)
        {
            var unmodifiedPlaybackResult = await postStopSuspendSoundPlayer.PlayAsync(postStopSuspendSound, cancellationToken);
            return PostStopSuspendSoundPlaybackResult.FromPlaybackResult(unmodifiedPlaybackResult);
        }

        var warningResults = new List<LidGuardOperationResult>();
        var captureResult = systemAudioVolumeController.CaptureDefaultRenderDeviceState();
        if (!captureResult.Succeeded)
        {
            warningResults.Add(LidGuardOperationResult.Failure(
                $"Post-stop suspend sound volume override skipped because the current system audio volume could not be captured: {captureResult.Message}"));
            var uncapturedPlaybackResult = await postStopSuspendSoundPlayer.PlayAsync(postStopSuspendSound, cancellationToken);
            return PostStopSuspendSoundPlaybackResult.FromPlaybackResult(uncapturedPlaybackResult, [.. warningResults]);
        }

        LidGuardOperationResult playbackResult;
        try
        {
            var applyResult = systemAudioVolumeController.ApplyDefaultRenderDeviceVolumeOverride(postStopSuspendSoundVolumeOverridePercent.Value);
            if (!applyResult.Succeeded) warningResults.Add(LidGuardOperationResult.Failure($"Post-stop suspend sound volume override could not be applied; playback will continue with the current system audio state: {applyResult.Message}"));

            playbackResult = await postStopSuspendSoundPlayer.PlayAsync(postStopSuspendSound, cancellationToken);
        }
        finally
        {
            var restoreResult = systemAudioVolumeController.RestoreDefaultRenderDeviceState(captureResult.Value);
            if (!restoreResult.Succeeded) warningResults.Add(LidGuardOperationResult.Failure($"Post-stop suspend sound volume state could not be restored after playback: {restoreResult.Message}"));
        }

        return PostStopSuspendSoundPlaybackResult.FromPlaybackResult(playbackResult, [.. warningResults]);
    }
}

internal sealed class PostStopSuspendSoundPlaybackResult
{
    public LidGuardOperationResult PlaybackResult { get; init; } = LidGuardOperationResult.Success();

    public LidGuardOperationResult[] VolumeWarningResults { get; init; } = [];

    public static PostStopSuspendSoundPlaybackResult Success() => new();

    public static PostStopSuspendSoundPlaybackResult FromPlaybackResult(
        LidGuardOperationResult playbackResult,
        LidGuardOperationResult[] volumeWarningResults = null)
        => new()
        {
            PlaybackResult = playbackResult,
            VolumeWarningResults = volumeWarningResults ?? []
        };
}
