namespace LidGuard.Hooks;

internal sealed class HookEventLog(string logFileName)
{
    public string GetDefaultLogFilePath() => HookEventLogWriter.GetDefaultLogFilePath(logFileName);

    public TimeSpan AppendReceived(
        string hookEventName,
        IHookCommandInput hookInput,
        string providerDetails,
        bool includePrompt)
    {
        ArgumentNullException.ThrowIfNull(hookInput);

        var details = $"transcriptPath={HookEventLogWriter.Sanitize(hookInput.TranscriptPath)}";
        if (!string.IsNullOrWhiteSpace(providerDetails)) details = $"{details} {providerDetails}";
        if (includePrompt) details = $"{details} prompt={HookEventLogWriter.Sanitize(hookInput.Prompt)}";

        return AppendLine(HookEventLogWriter.CreateLogLine(
            "received",
            hookEventName,
            hookInput.SessionIdentifier,
            hookInput.WorkingDirectory,
            details));
    }

    public TimeSpan AppendRuntimeResult(
        string hookEventName,
        IHookCommandInput hookInput,
        string commandName,
        bool succeeded,
        bool runtimeUnavailable,
        int activeSessionCount,
        string message,
        string timingDetails = "")
    {
        ArgumentNullException.ThrowIfNull(hookInput);

        var details =
            $"command={HookEventLogWriter.Sanitize(commandName)} transcriptPath={HookEventLogWriter.Sanitize(hookInput.TranscriptPath)} "
            + $"succeeded={succeeded} runtimeUnavailable={runtimeUnavailable} activeSessions={activeSessionCount} message={HookEventLogWriter.Sanitize(message)}";
        if (!string.IsNullOrWhiteSpace(timingDetails)) details = $"{details} {HookEventLogWriter.Sanitize(timingDetails)}";

        return AppendLine(HookEventLogWriter.CreateLogLine(
            "runtime-result",
            hookEventName,
            hookInput.SessionIdentifier,
            hookInput.WorkingDirectory,
            details));
    }

    public void AppendMessage(string message)
        => AppendLine(HookEventLogWriter.CreateLogLine("message", string.Empty, string.Empty, string.Empty, HookEventLogWriter.Sanitize(message)));

    public IReadOnlyList<string> ReadRecentLines(int maximumLineCount) => HookEventLogWriter.ReadRecentLines(GetDefaultLogFilePath(), maximumLineCount);

    public static string CreateDetails(params (string FieldName, string FieldValue)[] fields)
        => string.Join(" ", fields.Select(field => $"{field.FieldName}={HookEventLogWriter.Sanitize(field.FieldValue)}"));

    private TimeSpan AppendLine(string line) => HookEventLogWriter.AppendLine(GetDefaultLogFilePath(), line);
}
