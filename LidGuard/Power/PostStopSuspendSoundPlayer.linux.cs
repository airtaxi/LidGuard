using LidGuard.Platform;
using LidGuard.Results;
using LidGuard.Services;

namespace LidGuard.Power;

public sealed class PostStopSuspendSoundPlayer : IPostStopSuspendSoundPlayer
{
    private static readonly Dictionary<string, string[]> s_systemSoundCandidates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Asterisk"] = ["dialog-information", "message", "bell"],
        ["Beep"] = ["bell", "audio-volume-change", "message"],
        ["Exclamation"] = ["dialog-warning", "bell"],
        ["Hand"] = ["dialog-error", "dialog-warning"],
        ["Question"] = ["dialog-question", "dialog-information"]
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

        if (!TryFindAudioPlayer(out var audioPlayer))
            return LidGuardOperationResult.Failure("No supported Linux audio player was found. Install pw-play, paplay, or aplay to use post-stop suspend sounds.");

        var soundPath = normalizeResult.Value;
        if (TryGetCanonicalSystemSoundName(normalizeResult.Value, out var canonicalSystemSoundName))
        {
            if (!TryResolveSystemSoundPath(canonicalSystemSoundName, audioPlayer, out soundPath))
                return LidGuardOperationResult.Failure($"Could not find a Linux desktop sound-theme file for system sound {canonicalSystemSoundName}.");
        }

        var commandResult = await LinuxCommandRunner.RunAsync(audioPlayer.ExecutablePath, [soundPath], cancellationToken);
        if (commandResult.Succeeded) return LidGuardOperationResult.Success();

        return LidGuardOperationResult.Failure(commandResult.CreateFailureMessage(Path.GetFileName(audioPlayer.ExecutablePath)));
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
            var supportedSystemSounds = string.Join(", ", s_systemSoundCandidates.Keys.OrderBy(static key => key, StringComparer.Ordinal));
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

    private static bool TryFindAudioPlayer(out LinuxAudioPlayer audioPlayer)
    {
        if (LinuxCommandPathResolver.TryFindExecutable("pw-play", out var pipeWirePlayerPath))
        {
            audioPlayer = new LinuxAudioPlayer(pipeWirePlayerPath, true);
            return true;
        }

        if (LinuxCommandPathResolver.TryFindExecutable("paplay", out var pulseAudioPlayerPath))
        {
            audioPlayer = new LinuxAudioPlayer(pulseAudioPlayerPath, true);
            return true;
        }

        if (LinuxCommandPathResolver.TryFindExecutable("aplay", out var advancedLinuxSoundArchitecturePlayerPath))
        {
            audioPlayer = new LinuxAudioPlayer(advancedLinuxSoundArchitecturePlayerPath, false);
            return true;
        }

        audioPlayer = default;
        return false;
    }

    private static bool TryResolveSystemSoundPath(string canonicalSystemSoundName, LinuxAudioPlayer audioPlayer, out string soundPath)
    {
        soundPath = string.Empty;
        var candidateSoundNames = s_systemSoundCandidates[canonicalSystemSoundName];
        string[] extensions = audioPlayer.SupportsCompressedAudio ? [".wav", ".oga", ".ogg"] : [".wav"];

        foreach (var dataDirectoryPath in GetDataDirectoryPaths())
        {
            var soundsDirectoryPath = Path.Combine(dataDirectoryPath, "sounds");
            if (!Directory.Exists(soundsDirectoryPath)) continue;

            foreach (var candidateSoundName in candidateSoundNames)
            {
                foreach (var extension in extensions)
                {
                    foreach (var candidatePath in EnumerateSoundFiles(soundsDirectoryPath, candidateSoundName + extension))
                    {
                        soundPath = candidatePath;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static IEnumerable<string> GetDataDirectoryPaths()
    {
        var dataDirectoryPaths = new List<string>();
        var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfilePath)) dataDirectoryPaths.Add(Path.Combine(userProfilePath, ".local", "share"));

        var configuredDataDirectoryPaths = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
        if (string.IsNullOrWhiteSpace(configuredDataDirectoryPaths)) configuredDataDirectoryPaths = "/usr/local/share:/usr/share";
        dataDirectoryPaths.AddRange(configuredDataDirectoryPaths.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return dataDirectoryPaths.Distinct(StringComparer.Ordinal);
    }

    private static IEnumerable<string> EnumerateSoundFiles(string soundsDirectoryPath, string fileName)
    {
        try { return Directory.EnumerateFiles(soundsDirectoryPath, fileName, SearchOption.AllDirectories).ToArray(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return []; }
    }

    private static bool TryGetCanonicalSystemSoundName(string configuredValue, out string canonicalSystemSoundName)
    {
        foreach (var systemSoundName in s_systemSoundCandidates.Keys)
        {
            if (!configuredValue.Equals(systemSoundName, StringComparison.OrdinalIgnoreCase)) continue;

            canonicalSystemSoundName = systemSoundName;
            return true;
        }

        canonicalSystemSoundName = string.Empty;
        return false;
    }

    private readonly record struct LinuxAudioPlayer(string ExecutablePath, bool SupportsCompressedAudio);
}
