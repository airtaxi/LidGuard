using System.Text.Json;
using LidGuard.Settings;

namespace LidGuard.Runtime;

internal static class LidGuardRuntimeSessionLogStore
{
    private const int MaximumEntryCount = 500;
    private const string LogFileName = "session-execution.log";
    private static readonly object s_gate = new();

    public static string GetDefaultLogFilePath() => Path.Combine(LidGuardSettingsStore.GetApplicationDataDirectoryPath(), LogFileName);

    public static void Append(LidGuardRuntimeSessionLogEntry entry)
    {
        try
        {
            lock (s_gate)
            {
                var logFilePath = GetDefaultLogFilePath();
                var logDirectoryPath = Path.GetDirectoryName(logFilePath);
                if (!string.IsNullOrWhiteSpace(logDirectoryPath)) Directory.CreateDirectory(logDirectoryPath);

                var entryJson = JsonSerializer.Serialize(entry, LidGuardRuntimeSessionLogJsonSerializerContext.Default.LidGuardRuntimeSessionLogEntry);
                var logLines = File.Exists(logFilePath)
                    ? File.ReadAllLines(logFilePath).Where(line => !string.IsNullOrWhiteSpace(line)).ToList()
                    : [];

                logLines.Add(entryJson);
                if (logLines.Count > MaximumEntryCount) logLines = logLines.Skip(logLines.Count - MaximumEntryCount).ToList();

                File.WriteAllLines(logFilePath, logLines);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}

