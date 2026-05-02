using System.Text.Json;
using LidGuard.Settings;

namespace LidGuard.Runtime;

internal static class MacOSPendingPowerStateBackupStore
{
    private const string PendingBackupFileName = "pending-macos-power-state-backup.json";
    private const string TemporaryFileExtension = ".tmp";
    private static readonly object s_gate = new();

    public static string GetDefaultFilePath() => Path.Combine(LidGuardSettingsStore.GetApplicationDataDirectoryPath(), PendingBackupFileName);

    public static bool TryLoad(out MacOSPendingPowerStateBackupState state, out bool hasBackup, out string message)
    {
        state = null;
        hasBackup = false;
        message = string.Empty;

        var pendingBackupFilePath = GetDefaultFilePath();

        try
        {
            lock (s_gate)
            {
                if (!File.Exists(pendingBackupFilePath)) return true;

                var content = File.ReadAllText(pendingBackupFilePath);
                state = JsonSerializer.Deserialize(content, MacOSPendingPowerStateBackupJsonSerializerContext.Default.MacOSPendingPowerStateBackupState);
                if (state is null)
                {
                    message = $"Failed to read LidGuard pending macOS power-state backup from {pendingBackupFilePath}: The file did not contain a valid backup.";
                    return false;
                }

                hasBackup = true;
                return true;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            message = $"Failed to read LidGuard pending macOS power-state backup from {pendingBackupFilePath}: {exception.Message}";
            return false;
        }
    }

    public static bool TrySave(MacOSPendingPowerStateBackupState state, out string message)
    {
        var pendingBackupFilePath = GetDefaultFilePath();
        var temporaryFilePath = pendingBackupFilePath + TemporaryFileExtension;

        try
        {
            lock (s_gate)
            {
                var pendingBackupDirectoryPath = Path.GetDirectoryName(pendingBackupFilePath);
                if (!string.IsNullOrWhiteSpace(pendingBackupDirectoryPath)) Directory.CreateDirectory(pendingBackupDirectoryPath);

                var content = JsonSerializer.Serialize(state, MacOSPendingPowerStateBackupJsonSerializerContext.Default.MacOSPendingPowerStateBackupState);
                File.WriteAllText(temporaryFilePath, content);
                File.Move(temporaryFilePath, pendingBackupFilePath, true);
            }

            message = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            message = $"Failed to write LidGuard pending macOS power-state backup to {pendingBackupFilePath}: {exception.Message}";
            return false;
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryFilePath);
        }
    }

    public static bool TryDelete(out string message)
    {
        var pendingBackupFilePath = GetDefaultFilePath();

        try
        {
            lock (s_gate)
            {
                if (!File.Exists(pendingBackupFilePath))
                {
                    message = string.Empty;
                    return true;
                }

                File.Delete(pendingBackupFilePath);
            }

            message = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            message = $"Failed to delete LidGuard pending macOS power-state backup at {pendingBackupFilePath}: {exception.Message}";
            return false;
        }
    }

    private static void TryDeleteTemporaryFile(string temporaryFilePath)
    {
        try
        {
            if (File.Exists(temporaryFilePath)) File.Delete(temporaryFilePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
