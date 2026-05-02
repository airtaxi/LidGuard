using System.Globalization;
using LidGuard.Platform;
using LidGuard.Results;
using LidGuard.Services;
using LidGuard.Settings;

namespace LidGuard.Audio;

public sealed class SystemAudioVolumeController : ISystemAudioVolumeController
{
    private static readonly TimeSpan s_osascriptTimeout = TimeSpan.FromSeconds(5);

    public LidGuardOperationResult<SystemAudioVolumeState> CaptureDefaultRenderDeviceState()
    {
        if (!TryFindOsascript(out var osascriptPath, out var message)) return LidGuardOperationResult<SystemAudioVolumeState>.Failure(message);

        var volumeResult = MacOSCommandRunner.Run(
            osascriptPath,
            ["-e", "output volume of (get volume settings)"],
            s_osascriptTimeout);
        if (!volumeResult.Succeeded) return LidGuardOperationResult<SystemAudioVolumeState>.Failure(volumeResult.CreateFailureMessage("osascript output volume"));

        var muteResult = MacOSCommandRunner.Run(
            osascriptPath,
            ["-e", "output muted of (get volume settings)"],
            s_osascriptTimeout);
        if (!muteResult.Succeeded) return LidGuardOperationResult<SystemAudioVolumeState>.Failure(muteResult.CreateFailureMessage("osascript output muted"));

        if (!int.TryParse(volumeResult.StandardOutput.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var volumePercent))
            return LidGuardOperationResult<SystemAudioVolumeState>.Failure("Failed to parse macOS output volume.");
        if (!TryParseMuteState(muteResult.StandardOutput, out var isMuted))
            return LidGuardOperationResult<SystemAudioVolumeState>.Failure("Failed to parse macOS output mute state.");

        return LidGuardOperationResult<SystemAudioVolumeState>.Success(new SystemAudioVolumeState
        {
            MasterVolumeScalar = Math.Clamp(volumePercent, 0, 100) / 100.0f,
            IsMuted = isMuted
        });
    }

    public LidGuardOperationResult ApplyDefaultRenderDeviceVolumeOverride(int volumeOverridePercent)
    {
        if (!LidGuardSettings.IsValidPostStopSuspendSoundVolumeOverridePercent(volumeOverridePercent))
            return LidGuardOperationResult.Failure($"The volume override percent must be an integer from {LidGuardSettings.MinimumPostStopSuspendSoundVolumeOverridePercent} through {LidGuardSettings.MaximumPostStopSuspendSoundVolumeOverridePercent}.");
        if (!TryFindOsascript(out var osascriptPath, out var message)) return LidGuardOperationResult.Failure(message);

        var commandResult = MacOSCommandRunner.Run(
            osascriptPath,
            ["-e", $"set volume output volume {volumeOverridePercent} without output muted"],
            s_osascriptTimeout);
        if (commandResult.Succeeded) return LidGuardOperationResult.Success();

        return LidGuardOperationResult.Failure(commandResult.CreateFailureMessage("osascript set volume"), commandResult.ExitCode);
    }

    public LidGuardOperationResult RestoreDefaultRenderDeviceState(SystemAudioVolumeState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!TryFindOsascript(out var osascriptPath, out var message)) return LidGuardOperationResult.Failure(message);

        var volumePercent = Math.Clamp((int)Math.Round(state.MasterVolumeScalar * 100.0f, MidpointRounding.AwayFromZero), 0, 100);
        var muteValue = state.IsMuted ? "true" : "false";
        var volumeResult = MacOSCommandRunner.Run(
            osascriptPath,
            ["-e", $"set volume output volume {volumePercent}"],
            s_osascriptTimeout);
        var muteResult = MacOSCommandRunner.Run(
            osascriptPath,
            ["-e", $"set volume output muted {muteValue}"],
            s_osascriptTimeout);
        return CombineVolumeChangeResults(
            volumeResult.Succeeded ? LidGuardOperationResult.Success() : LidGuardOperationResult.Failure(volumeResult.CreateFailureMessage("osascript set volume"), volumeResult.ExitCode),
            muteResult.Succeeded ? LidGuardOperationResult.Success() : LidGuardOperationResult.Failure(muteResult.CreateFailureMessage("osascript set muted"), muteResult.ExitCode));
    }

    private static bool TryFindOsascript(out string osascriptPath, out string message)
    {
        if (MacOSCommandPathResolver.TryFindExecutable("osascript", out osascriptPath))
        {
            message = string.Empty;
            return true;
        }

        message = "osascript was not found on PATH. macOS post-stop sound volume override requires /usr/bin/osascript.";
        return false;
    }

    private static bool TryParseMuteState(string muteOutput, out bool isMuted)
    {
        isMuted = false;
        var trimmedMuteOutput = muteOutput.Trim();
        if (trimmedMuteOutput.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            isMuted = true;
            return true;
        }

        if (trimmedMuteOutput.Equals("false", StringComparison.OrdinalIgnoreCase)) return true;
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
