using System.Text.Json;
using LidGuard.Settings;
using LidGuardLib.Commons.Settings;

namespace LidGuard.Runtime;

internal static class SuspendHistoryLogStore
{
    private const string LogFileName = "suspend-history.log";
    private static readonly object s_gate = new();

    public static string GetDefaultLogFilePath() => Path.Combine(LidGuardSettingsStore.GetApplicationDataDirectoryPath(), LogFileName);

    public static void Append(SuspendHistoryEntry entry, int? maximumEntryCount)
    {
        if (maximumEntryCount is null) return;

        var normalizedMaximumEntryCount = Math.Max(LidGuardSettings.MinimumSuspendHistoryEntryCount, maximumEntryCount.Value);
        try
        {
            lock (s_gate)
            {
                var logFilePath = GetDefaultLogFilePath();
                var logDirectoryPath = Path.GetDirectoryName(logFilePath);
                if (!string.IsNullOrWhiteSpace(logDirectoryPath)) Directory.CreateDirectory(logDirectoryPath);

                var entryJson = JsonSerializer.Serialize(entry, SuspendHistoryJsonSerializerContext.Default.SuspendHistoryEntry);
                var logLines = File.Exists(logFilePath)
                    ? File.ReadAllLines(logFilePath).Where(line => !string.IsNullOrWhiteSpace(line)).ToList()
                    : [];

                logLines.Add(entryJson);
                if (logLines.Count > normalizedMaximumEntryCount) logLines = logLines.Skip(logLines.Count - normalizedMaximumEntryCount).ToList();

                File.WriteAllLines(logFilePath, logLines);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { }
    }

    public static bool TryReadRecent(int entryCount, out SuspendHistoryEntry[] entries, out string message)
    {
        entries = [];
        message = string.Empty;
        if (entryCount < LidGuardSettings.MinimumSuspendHistoryEntryCount)
        {
            message = $"Suspend history count must be an integer of at least {LidGuardSettings.MinimumSuspendHistoryEntryCount}.";
            return false;
        }

        try
        {
            var logFilePath = GetDefaultLogFilePath();
            if (!File.Exists(logFilePath)) return true;

            var logLines = File.ReadAllLines(logFilePath).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            var firstLineIndex = Math.Max(0, logLines.Length - entryCount);
            var historyEntries = new List<SuspendHistoryEntry>();
            for (var lineIndex = logLines.Length - 1; lineIndex >= firstLineIndex; lineIndex--)
            {
                try
                {
                    var historyEntry = JsonSerializer.Deserialize(
                        logLines[lineIndex],
                        SuspendHistoryJsonSerializerContext.Default.SuspendHistoryEntry);
                    if (historyEntry is not null) historyEntries.Add(historyEntry);
                }
                catch (JsonException) { }
            }

            entries = [.. historyEntries];
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            message = $"Failed to read suspend history from {GetDefaultLogFilePath()}: {exception.Message}";
            return false;
        }
    }
}
