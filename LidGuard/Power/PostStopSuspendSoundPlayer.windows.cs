using System.IO;
using System.Media;
using System.Runtime.Versioning;
using LidGuard.Results;
using LidGuard.Services;
using Windows.Win32;
using Windows.Win32.Media.Audio;

namespace LidGuard.Power;

[SupportedOSPlatform("windows6.1")]
public sealed partial class PostStopSuspendSoundPlayer : IPostStopSuspendSoundPlayer
{
    private const SND_FLAGS SystemSoundFlags = SND_FLAGS.SND_ALIAS | SND_FLAGS.SND_SYNC;
    private static readonly Dictionary<string, string> s_systemSoundAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Asterisk"] = "SystemAsterisk",
        ["Beep"] = "SystemDefault",
        ["Exclamation"] = "SystemExclamation",
        ["Hand"] = "SystemHand",
        ["Question"] = "SystemQuestion"
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

        cancellationToken.ThrowIfCancellationRequested();
        if (TryGetCanonicalSystemSoundName(normalizeResult.Value, out var canonicalSystemSoundName))
            return await Task.Run(() => PlaySystemSound(canonicalSystemSoundName), cancellationToken);

        return await Task.Run(() => PlayWaveFile(normalizeResult.Value), cancellationToken);
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
            var supportedSystemSounds = string.Join(", ", s_systemSoundAliases.Keys.OrderBy(static key => key, StringComparer.Ordinal));
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

        try
        {
            using var soundPlayer = new SoundPlayer(fullWaveFilePath);
            soundPlayer.Load();
            return LidGuardOperationResult<string>.Success(fullWaveFilePath);
        }
        catch (Exception exception) when (exception is FileNotFoundException or InvalidOperationException or TimeoutException or UnauthorizedAccessException or IOException)
        {
            return LidGuardOperationResult<string>.Failure($"The configured WAV file could not be loaded as a playable WAV file: {exception.Message}");
        }
    }

    private static LidGuardOperationResult PlaySystemSound(string canonicalSystemSoundName)
    {
        var systemSoundAlias = s_systemSoundAliases[canonicalSystemSoundName];
        if (PInvoke.PlaySound(systemSoundAlias, null, SystemSoundFlags)) return LidGuardOperationResult.Success();

        return LidGuardOperationResult.Failure($"Failed to play the configured system sound: {canonicalSystemSoundName}");
    }

    private static LidGuardOperationResult PlayWaveFile(string waveFilePath)
    {
        try
        {
            using var soundPlayer = new SoundPlayer(waveFilePath);
            soundPlayer.Load();
            soundPlayer.PlaySync();
            return LidGuardOperationResult.Success();
        }
        catch (Exception exception) when (exception is FileNotFoundException or InvalidOperationException or TimeoutException or UnauthorizedAccessException or IOException)
        {
            return LidGuardOperationResult.Failure($"Failed to play the configured WAV file: {exception.Message}");
        }
    }

    private static bool TryGetCanonicalSystemSoundName(string configuredValue, out string canonicalSystemSoundName)
    {
        foreach (var systemSoundName in s_systemSoundAliases.Keys)
        {
            if (!configuredValue.Equals(systemSoundName, StringComparison.OrdinalIgnoreCase)) continue;

            canonicalSystemSoundName = systemSoundName;
            return true;
        }

        canonicalSystemSoundName = string.Empty;
        return false;
    }

}
