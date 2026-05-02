using LidGuard.Platform;
using LidGuard.Results;
using LidGuard.Services;
using LidGuard.Settings;

namespace LidGuard.Audio;

public sealed class SystemAudioVolumeController : ISystemAudioVolumeController
{
    private static readonly TimeSpan s_pactlTimeout = TimeSpan.FromSeconds(5);

    public LidGuardOperationResult<SystemAudioVolumeState> CaptureDefaultRenderDeviceState()
    {
        if (!TryFindPactl(out var pactlPath, out var message)) return LidGuardOperationResult<SystemAudioVolumeState>.Failure(message);

        var volumeResult = LinuxCommandRunner.Run(pactlPath, ["get-sink-volume", "@DEFAULT_SINK@"], s_pactlTimeout);
        if (!volumeResult.Succeeded) return LidGuardOperationResult<SystemAudioVolumeState>.Failure(volumeResult.CreateFailureMessage("pactl get-sink-volume"));

        var muteResult = LinuxCommandRunner.Run(pactlPath, ["get-sink-mute", "@DEFAULT_SINK@"], s_pactlTimeout);
        if (!muteResult.Succeeded) return LidGuardOperationResult<SystemAudioVolumeState>.Failure(muteResult.CreateFailureMessage("pactl get-sink-mute"));

        if (!TryParseVolumePercent(volumeResult.StandardOutput, out var volumePercent))
            return LidGuardOperationResult<SystemAudioVolumeState>.Failure("Failed to parse pactl default sink volume.");
        if (!TryParseMuteState(muteResult.StandardOutput, out var isMuted))
            return LidGuardOperationResult<SystemAudioVolumeState>.Failure("Failed to parse pactl default sink mute state.");

        return LidGuardOperationResult<SystemAudioVolumeState>.Success(new SystemAudioVolumeState
        {
            MasterVolumeScalar = volumePercent / 100.0f,
            IsMuted = isMuted
        });
    }

    public LidGuardOperationResult ApplyDefaultRenderDeviceVolumeOverride(int volumeOverridePercent)
    {
        if (!LidGuardSettings.IsValidPostStopSuspendSoundVolumeOverridePercent(volumeOverridePercent))
            return LidGuardOperationResult.Failure($"The volume override percent must be an integer from {LidGuardSettings.MinimumPostStopSuspendSoundVolumeOverridePercent} through {LidGuardSettings.MaximumPostStopSuspendSoundVolumeOverridePercent}.");
        if (!TryFindPactl(out var pactlPath, out var message)) return LidGuardOperationResult.Failure(message);

        var volumeResult = LinuxCommandRunner.Run(pactlPath, ["set-sink-volume", "@DEFAULT_SINK@", $"{volumeOverridePercent}%"], s_pactlTimeout);
        var muteResult = LinuxCommandRunner.Run(pactlPath, ["set-sink-mute", "@DEFAULT_SINK@", "0"], s_pactlTimeout);
        return CombineVolumeChangeResults(
            volumeResult.Succeeded ? LidGuardOperationResult.Success() : LidGuardOperationResult.Failure(volumeResult.CreateFailureMessage("pactl set-sink-volume"), volumeResult.ExitCode),
            muteResult.Succeeded ? LidGuardOperationResult.Success() : LidGuardOperationResult.Failure(muteResult.CreateFailureMessage("pactl set-sink-mute"), muteResult.ExitCode));
    }

    public LidGuardOperationResult RestoreDefaultRenderDeviceState(SystemAudioVolumeState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!TryFindPactl(out var pactlPath, out var message)) return LidGuardOperationResult.Failure(message);

        var volumePercent = Math.Max(0, (int)Math.Round(state.MasterVolumeScalar * 100.0f, MidpointRounding.AwayFromZero));
        var muteValue = state.IsMuted ? "1" : "0";
        var volumeResult = LinuxCommandRunner.Run(pactlPath, ["set-sink-volume", "@DEFAULT_SINK@", $"{volumePercent}%"], s_pactlTimeout);
        var muteResult = LinuxCommandRunner.Run(pactlPath, ["set-sink-mute", "@DEFAULT_SINK@", muteValue], s_pactlTimeout);
        return CombineVolumeChangeResults(
            volumeResult.Succeeded ? LidGuardOperationResult.Success() : LidGuardOperationResult.Failure(volumeResult.CreateFailureMessage("pactl set-sink-volume"), volumeResult.ExitCode),
            muteResult.Succeeded ? LidGuardOperationResult.Success() : LidGuardOperationResult.Failure(muteResult.CreateFailureMessage("pactl set-sink-mute"), muteResult.ExitCode));
    }

    private static bool TryFindPactl(out string pactlPath, out string message)
    {
        if (LinuxCommandPathResolver.TryFindExecutable("pactl", out pactlPath))
        {
            message = string.Empty;
            return true;
        }

        message = "pactl was not found on PATH. Linux post-stop sound volume override requires PulseAudio or PipeWire PulseAudio compatibility.";
        return false;
    }

    private static bool TryParseVolumePercent(string volumeOutput, out int volumePercent)
    {
        volumePercent = 0;
        foreach (var token in volumeOutput.Split([' ', '\t', '\r', '\n', '/', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!token.EndsWith('%')) continue;
            if (!int.TryParse(token[..^1], out volumePercent)) continue;

            return volumePercent >= 0;
        }

        return false;
    }

    private static bool TryParseMuteState(string muteOutput, out bool isMuted)
    {
        isMuted = false;
        if (muteOutput.Contains("yes", StringComparison.OrdinalIgnoreCase))
        {
            isMuted = true;
            return true;
        }

        if (muteOutput.Contains("no", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static LidGuardOperationResult CombineVolumeChangeResults(params LidGuardOperationResult[] results)
    {
        var failedResults = results.Where(static result => !result.Succeeded).ToArray();
        if (failedResults.Length == 0) return LidGuardOperationResult.Success();

        var message = string.Join(" ", failedResults.Select(static result => result.Message));
        var nativeErrorCode = failedResults.FirstOrDefault(static result => result.NativeErrorCode != 0)?.NativeErrorCode ?? 0;
        return LidGuardOperationResult.Failure(message, nativeErrorCode);
    }
}
