namespace LidGuard.Hooks;

public static class ClaudeHookEventLog
{
    private const string LogFileName = "claude-hook-events.log";

    private static readonly HookEventLog s_eventLog = new(LogFileName);

    public static string GetDefaultLogFilePath() => s_eventLog.GetDefaultLogFilePath();

    public static TimeSpan AppendReceived(ClaudeHookInput hookInput)
    {
        ArgumentNullException.ThrowIfNull(hookInput);

        var details = HookEventLog.CreateDetails(
            ("permissionMode", hookInput.PermissionMode),
            ("tool", hookInput.ToolName),
            ("toolUseId", hookInput.ToolUseIdentifier),
            ("agentId", hookInput.AgentIdentifier),
            ("agentType", hookInput.AgentType),
            ("taskId", hookInput.TaskIdentifier),
            ("reason", hookInput.Reason),
            ("notificationType", hookInput.NotificationType),
            ("isInterrupt", hookInput.IsInterrupt.ToString()),
            ("stopHookActive", hookInput.StopHookActive.ToString()));
        return s_eventLog.AppendReceived(hookInput.HookEventName, hookInput, details, IsUserPromptSubmitEvent(hookInput.HookEventName));
    }

    public static TimeSpan AppendRuntimeResult(
        ClaudeHookInput hookInput,
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

    private static bool IsUserPromptSubmitEvent(string hookEventName) => string.Equals(hookEventName?.Trim(), ClaudeHookEventNames.UserPromptSubmit, StringComparison.Ordinal);
}
