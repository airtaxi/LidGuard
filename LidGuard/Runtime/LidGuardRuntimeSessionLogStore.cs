using System.Text.Json;
using LidGuard.Settings;

namespace LidGuard.Runtime;

internal static class LidGuardRuntimeSessionLogStore
{
    private const int MaximumEntryCount = 500;
    private const string LogFileName = "session-execution.log";
    private static readonly object s_gate = new();

    public static event Action Appended;

    public static string GetDefaultLogFilePath() => Path.Combine(LidGuardSettingsStore.GetApplicationDataDirectoryPath(), LogFileName);

    public static void Append(LidGuardRuntimeSessionLogEntry entry)
    {
        var appended = false;
        try
        {
            lock (s_gate)
            {
                var logFilePath = GetDefaultLogFilePath();
                var logDirectoryPath = Path.GetDirectoryName(logFilePath);
                if (!string.IsNullOrWhiteSpace(logDirectoryPath)) Directory.CreateDirectory(logDirectoryPath);

                var entryJson = JsonSerializer.Serialize(entry, LidGuardRuntimeSessionLogJsonSerializerContext.Default.LidGuardRuntimeSessionLogEntry);
                var logLines = File.Exists(logFilePath) ? File.ReadAllLines(logFilePath).Where(line => !string.IsNullOrWhiteSpace(line)).ToList() : [];

                logLines.Add(entryJson);
                if (logLines.Count > MaximumEntryCount) logLines = logLines.Skip(logLines.Count - MaximumEntryCount).ToList();

                File.WriteAllLines(logFilePath, logLines);
                appended = true;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }

        if (appended) NotifyAppended();
    }

    public static bool TryReadRecent(int entryCount, out LidGuardRuntimeSessionLogEntry[] entries, out string message)
    {
        entries = [];
        message = string.Empty;
        if (entryCount <= 0)
        {
            message = "Runtime session log count must be a positive integer.";
            return false;
        }

        try
        {
            var logFilePath = GetDefaultLogFilePath();
            if (!File.Exists(logFilePath)) return true;

            var logLines = File.ReadAllLines(logFilePath).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            var firstLineIndex = Math.Max(0, logLines.Length - entryCount);
            var logEntries = new List<LidGuardRuntimeSessionLogEntry>();
            for (var lineIndex = logLines.Length - 1; lineIndex >= firstLineIndex; lineIndex--)
            {
                try
                {
                    var logEntry = JsonSerializer.Deserialize(logLines[lineIndex], LidGuardRuntimeSessionLogJsonSerializerContext.Default.LidGuardRuntimeSessionLogEntry);
                    if (logEntry is not null) logEntries.Add(logEntry);
                }
                catch (JsonException) { }
            }

            entries = [.. logEntries];
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            message = $"Failed to read runtime session log from {GetDefaultLogFilePath()}: {exception.Message}";
            return false;
        }
    }

    private static void NotifyAppended()
    {
        try { Appended?.Invoke(); }
        catch { }
    }
}
