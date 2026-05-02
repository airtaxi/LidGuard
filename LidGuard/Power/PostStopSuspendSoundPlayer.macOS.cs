using LidGuard.Platform;
using LidGuard.Results;
using LidGuard.Services;

namespace LidGuard.Power;

public sealed class PostStopSuspendSoundPlayer : IPostStopSuspendSoundPlayer
{
    private static readonly Dictionary<string, string> s_systemSoundFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Asterisk"] = "/System/Library/Sounds/Glass.aiff",
        ["Beep"] = "/System/Library/Sounds/Tink.aiff",
        ["Exclamation"] = "/System/Library/Sounds/Basso.aiff",
        ["Hand"] = "/System/Library/Sounds/Sosumi.aiff",
        ["Question"] = "/System/Library/Sounds/Ping.aiff"
    };

    public LidGuardOperationResult<string> NormalizeConfiguration(string configuredValue)
    {
        var trimmedConfiguredValue = configuredValue?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedConfiguredValue) || trimmedConfiguredValue.Equals("off", StringComparison.OrdinalIgnoreCase))
            return LidGuardOperationResult<string>.Success(string.Empty);

        if (TryGetCanonicalSystemSoundName(trimmedConfiguredValue, out var canonicalSystemSoundName))
            return LidGuardOperationResult<string>.Success(canonicalSystemSoundName);

        return NormalizeWaveFilePath(trimmedConfiguredValue);
    }

    public async Task<LidGuardOperationResult> PlayAsync(string configuredValue, CancellationToken cancellationToken)
    {
        var normalizeResult = NormalizeConfiguration(configuredValue);
        if (!normalizeResult.Succeeded) return LidGuardOperationResult.Failure(normalizeResult.Message);
        if (string.IsNullOrWhiteSpace(normalizeResult.Value)) return LidGuardOperationResult.Success();

        if (!MacOSCommandPathResolver.TryFindExecutable("afplay", out var audioPlayerPath))
            return LidGuardOperationResult.Failure("afplay was not found on PATH. LidGuard macOS post-stop suspend sounds require /usr/bin/afplay.");

        var soundPath = normalizeResult.Value;
        if (TryGetCanonicalSystemSoundName(normalizeResult.Value, out var canonicalSystemSoundName))
        {
            soundPath = s_systemSoundFiles[canonicalSystemSoundName];
            if (!File.Exists(soundPath)) return LidGuardOperationResult.Failure($"Could not find the macOS system sound file for {canonicalSystemSoundName}: {soundPath}");
        }

        var commandResult = await MacOSCommandRunner.RunAsync(audioPlayerPath, [soundPath], cancellationToken);
        if (commandResult.Succeeded) return LidGuardOperationResult.Success();

        return LidGuardOperationResult.Failure(commandResult.CreateFailureMessage("afplay"), commandResult.ExitCode);
    }

    private static LidGuardOperationResult<string> NormalizeWaveFilePath(string configuredValue)
    {
        string fullWaveFilePath;
        try { fullWaveFilePath = Path.GetFullPath(configuredValue); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return LidGuardOperationResult<string>.Failure($"The post-stop suspend sound path is invalid: {exception.Message}");
        }

        if (!string.Equals(Path.GetExtension(fullWaveFilePath), ".wav", StringComparison.OrdinalIgnoreCase))
        {
            var supportedSystemSounds = string.Join(", ", s_systemSoundFiles.Keys.OrderBy(static key => key, StringComparer.Ordinal));
            return LidGuardOperationResult<string>.Failure(
                $"The post-stop suspend sound must be off, one of {supportedSystemSounds}, or a path to a .wav file.");
        }

        if (Directory.Exists(fullWaveFilePath))
            return LidGuardOperationResult<string>.Failure($"The configured WAV path points to a directory, not a file: {fullWaveFilePath}");

        var waveFileDirectoryPath = Path.GetDirectoryName(fullWaveFilePath);
        if (string.IsNullOrWhiteSpace(waveFileDirectoryPath) || !Directory.Exists(waveFileDirectoryPath))
            return LidGuardOperationResult<string>.Failure($"The configured WAV directory does not exist: {waveFileDirectoryPath}");

        if (!File.Exists(fullWaveFilePath))
            return LidGuardOperationResult<string>.Failure($"The configured WAV file does not exist: {fullWaveFilePath}");

        return LidGuardOperationResult<string>.Success(fullWaveFilePath);
    }

    private static bool TryGetCanonicalSystemSoundName(string configuredValue, out string canonicalSystemSoundName)
    {
        foreach (var systemSoundName in s_systemSoundFiles.Keys)
        {
            if (!configuredValue.Equals(systemSoundName, StringComparison.OrdinalIgnoreCase)) continue;

            canonicalSystemSoundName = systemSoundName;
            return true;
        }

        canonicalSystemSoundName = string.Empty;
        return false;
    }
}
