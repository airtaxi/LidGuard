using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace LidGuard.Hooks;

internal static class HookEventLogWriter
{
    private const string LogDirectoryName = "LidGuard";

    public static string GetDefaultLogFilePath(string logFileName)
    {
        var localApplicationDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationDataPath)) localApplicationDataPath = Path.GetTempPath();
        return Path.Combine(localApplicationDataPath, LogDirectoryName, logFileName);
    }

    public static TimeSpan AppendLine(string logFilePath, string line)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var logDirectoryPath = Path.GetDirectoryName(logFilePath);
            if (!string.IsNullOrWhiteSpace(logDirectoryPath)) Directory.CreateDirectory(logDirectoryPath);
            File.AppendAllText(logFilePath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch { }
        finally { stopwatch.Stop(); }

        return stopwatch.Elapsed;
    }

    public static IReadOnlyList<string> ReadRecentLines(string logFilePath, int maximumLineCount)
    {
        if (maximumLineCount <= 0) return [];
        if (!File.Exists(logFilePath)) return [];

        try
        {
            var lines = File.ReadAllLines(logFilePath);
            if (lines.Length <= maximumLineCount) return lines;
            return lines[^maximumLineCount..];
        }
        catch { return[]; }
    }

    public static string CreateLogLine(string kind, string hookEventName, string sessionIdentifier, string workingDirectory, string details)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        return $"{timestamp} kind={Sanitize(kind)} event={Sanitize(hookEventName)} session={Sanitize(sessionIdentifier)} workingDirectory={Sanitize(workingDirectory)} {details}".TrimEnd();
    }

    public static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "<empty>";

        return value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal)
            .Trim();
    }
}
