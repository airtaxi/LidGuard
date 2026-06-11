namespace LidGuard.Hooks;

public static class OpenCodeHookEventLog
{
    private const string LogFileName = "opencode-hook-events.log";

    private static readonly HookEventLog s_eventLog = new(LogFileName);

    public static void AppendMessage(string message) => s_eventLog.AppendMessage(message);

    public static TimeSpan AppendReceived(OpenCodeHookInput hookInput)
    {
        ArgumentNullException.ThrowIfNull(hookInput);

        var details = HookEventLog.CreateDetails(("tool", hookInput.ToolName), ("permission", hookInput.Permission), ("status", hookInput.SessionStatus), ("stopHookActive", hookInput.StopHookActive.ToString()), ("messageID", hookInput.MessageIdentifier), ("callID", hookInput.CallIdentifier), ("eventID", hookInput.EventIdentifier));
        return s_eventLog.AppendReceived(hookInput.HookEventName, hookInput, details, hookInput.HookEventName.Equals(OpenCodeHookEventNames.ChatMessage, StringComparison.Ordinal));
    }

    public static TimeSpan AppendRuntimeResult(OpenCodeHookInput hookInput, string commandName, bool succeeded, bool runtimeUnavailable, int activeSessionCount, string message, string timingDetails = "")
    {
        ArgumentNullException.ThrowIfNull(hookInput);

        return s_eventLog.AppendRuntimeResult(hookInput.HookEventName, hookInput, commandName, succeeded, runtimeUnavailable, activeSessionCount, message, timingDetails);
    }

    public static string GetDefaultLogFilePath() => s_eventLog.GetDefaultLogFilePath();

    public static IReadOnlyList<string> ReadRecentLines(int maximumLineCount) => s_eventLog.ReadRecentLines(maximumLineCount);
}
