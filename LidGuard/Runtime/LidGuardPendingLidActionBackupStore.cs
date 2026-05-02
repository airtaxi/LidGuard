using System.Text.Json;
using LidGuard.Settings;
using LidGuard.Power;

namespace LidGuard.Runtime;

internal static class LidGuardPendingLidActionBackupStore
{
    private const string PendingBackupFileName = "pending-lid-action-backup.json";
    private const string TemporaryFileExtension = ".tmp";
    private static readonly object s_gate = new();

    public static string GetDefaultFilePath() => Path.Combine(LidGuardSettingsStore.GetApplicationDataDirectoryPath(), PendingBackupFileName);

    public static bool TryLoad(out LidActionBackup backup, out bool hasBackup, out string message)
    {
        backup = default;
        hasBackup = false;
        message = string.Empty;

        var pendingBackupFilePath = GetDefaultFilePath();

        try
        {
            lock (s_gate)
            {
                if (!File.Exists(pendingBackupFilePath)) return true;

                var content = File.ReadAllText(pendingBackupFilePath);
                var state = JsonSerializer.Deserialize(content, LidGuardPendingLidActionBackupJsonSerializerContext.Default.LidGuardPendingLidActionBackupState);
                if (state is null)
                {
                    message = $"Failed to read LidGuard pending lid action backup from {pendingBackupFilePath}: The file did not contain a valid backup.";
                    return false;
                }

                backup = state.ToBackup();
                hasBackup = true;
                return true;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            message = $"Failed to read LidGuard pending lid action backup from {pendingBackupFilePath}: {exception.Message}";
            return false;
        }
    }

    public static bool TrySave(LidActionBackup backup, out string message)
    {
        var pendingBackupFilePath = GetDefaultFilePath();
        var temporaryFilePath = pendingBackupFilePath + TemporaryFileExtension;

        try
        {
            lock (s_gate)
            {
                var pendingBackupDirectoryPath = Path.GetDirectoryName(pendingBackupFilePath);
                if (!string.IsNullOrWhiteSpace(pendingBackupDirectoryPath)) Directory.CreateDirectory(pendingBackupDirectoryPath);

                var state = LidGuardPendingLidActionBackupState.Create(backup);
                var content = JsonSerializer.Serialize(state, LidGuardPendingLidActionBackupJsonSerializerContext.Default.LidGuardPendingLidActionBackupState);
                File.WriteAllText(temporaryFilePath, content);
                File.Move(temporaryFilePath, pendingBackupFilePath, true);
            }

            message = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            message = $"Failed to write LidGuard pending lid action backup to {pendingBackupFilePath}: {exception.Message}";
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
            message = $"Failed to delete LidGuard pending lid action backup at {pendingBackupFilePath}: {exception.Message}";
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
