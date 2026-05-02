using System.Globalization;
using System.Text;
using LidGuardLib.Commons.Hooks;

namespace LidGuardLib.Hooks;

public static class ClaudeHookEventLog
{
    private const string LogDirectoryName = "LidGuard";
    private const string LogFileName = "claude-hook-events.log";

    public static string GetDefaultLogFilePath()
    {
        var localApplicationDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationDataPath)) localApplicationDataPath = Path.GetTempPath();
        return Path.Combine(localApplicationDataPath, LogDirectoryName, LogFileName);
    }

    public static void AppendReceived(ClaudeHookInput hookInput)
    {
        ArgumentNullException.ThrowIfNull(hookInput);

        var details =
            $"permissionMode={Sanitize(hookInput.PermissionMode)} tool={Sanitize(hookInput.ToolName)} reason={Sanitize(hookInput.Reason)} notificationType={Sanitize(hookInput.NotificationType)} transcriptPath={Sanitize(hookInput.TranscriptPath)} isInterrupt={hookInput.IsInterrupt}";
        if (IsUserPromptSubmitEvent(hookInput.HookEventName)) details = $"{details} prompt={Sanitize(hookInput.Prompt)}";

        AppendLine(CreateLogLine(
            "received",
            hookInput.HookEventName,
            hookInput.SessionIdentifier,
            hookInput.WorkingDirectory,
            details));
    }

    public static void AppendRuntimeResult(ClaudeHookInput hookInput, string commandName, bool succeeded, bool runtimeUnavailable, int activeSessionCount, string message)
    {
        ArgumentNullException.ThrowIfNull(hookInput);

        AppendLine(CreateLogLine(
            "runtime-result",
            hookInput.HookEventName,
            hookInput.SessionIdentifier,
            hookInput.WorkingDirectory,
            $"command={Sanitize(commandName)} transcriptPath={Sanitize(hookInput.TranscriptPath)} succeeded={succeeded} runtimeUnavailable={runtimeUnavailable} activeSessions={activeSessionCount} message={Sanitize(message)}"));
    }

    public static void AppendMessage(string message) => AppendLine(CreateLogLine("message", string.Empty, string.Empty, string.Empty, Sanitize(message)));

    public static IReadOnlyList<string> ReadRecentLines(int maximumLineCount)
    {
        if (maximumLineCount <= 0) return [];

        var logFilePath = GetDefaultLogFilePath();
        if (!File.Exists(logFilePath)) return [];

        try
        {
            var lines = File.ReadAllLines(logFilePath);
            if (lines.Length <= maximumLineCount) return lines;
            return lines[^maximumLineCount..];
        }
        catch
        {
            return [];
        }
    }

    private static void AppendLine(string line)
    {
        try
        {
            var logFilePath = GetDefaultLogFilePath();
            var logDirectoryPath = Path.GetDirectoryName(logFilePath);
            if (!string.IsNullOrWhiteSpace(logDirectoryPath)) Directory.CreateDirectory(logDirectoryPath);
            File.AppendAllText(logFilePath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
        }
    }

    private static string CreateLogLine(string kind, string hookEventName, string sessionIdentifier, string workingDirectory, string details)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        return $"{timestamp} kind={Sanitize(kind)} event={Sanitize(hookEventName)} session={Sanitize(sessionIdentifier)} workingDirectory={Sanitize(workingDirectory)} {details}".TrimEnd();
    }

    private static bool IsUserPromptSubmitEvent(string hookEventName) => string.Equals(hookEventName?.Trim(), ClaudeHookEventNames.UserPromptSubmit, StringComparison.Ordinal);

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "<empty>";

        return value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal)
            .Trim();
    }
}
