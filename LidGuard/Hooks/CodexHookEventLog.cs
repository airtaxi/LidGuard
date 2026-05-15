namespace LidGuard.Hooks;

public static class CodexHookEventLog
{
    private const string LogFileName = "codex-hook-events.log";

    private static readonly HookEventLog s_eventLog = new(LogFileName);

    public static string GetDefaultLogFilePath() => s_eventLog.GetDefaultLogFilePath();

    public static TimeSpan AppendReceived(CodexHookInput hookInput)
    {
        ArgumentNullException.ThrowIfNull(hookInput);

        var details = HookEventLog.CreateDetails(
            ("source", hookInput.Source),
            ("model", hookInput.Model));
        return s_eventLog.AppendReceived(hookInput.HookEventName, hookInput, details, IsUserPromptSubmitEvent(hookInput.HookEventName));
    }

    public static TimeSpan AppendRuntimeResult(
        CodexHookInput hookInput,
        string commandName,
        bool succeeded,
        bool runtimeUnavailable,
        int activeSessionCount,
        string message,
        string timingDetails = "")
    {
        ArgumentNullException.ThrowIfNull(hookInput);

        return s_eventLog.AppendRuntimeResult(
            hookInput.HookEventName,
            hookInput,
            commandName,
            succeeded,
            runtimeUnavailable,
            activeSessionCount,
            message,
            timingDetails);
    }

    public static void AppendMessage(string message) => s_eventLog.AppendMessage(message);

    public static IReadOnlyList<string> ReadRecentLines(int maximumLineCount) => s_eventLog.ReadRecentLines(maximumLineCount);

    private static bool IsUserPromptSubmitEvent(string hookEventName) => string.Equals(hookEventName?.Trim(), CodexHookEventNames.UserPromptSubmit, StringComparison.Ordinal);
}
