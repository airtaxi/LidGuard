namespace LidGuard.Hooks;

public static class GitHubCopilotHookEventLog
{
    private const string LogFileName = "copilot-hook-events.log";

    private static readonly HookEventLog s_eventLog = new(LogFileName);

    public static void AppendMessage(string message) => s_eventLog.AppendMessage(message);

    public static TimeSpan AppendReceived(string configuredHookEventName, GitHubCopilotHookInput hookInput)
    {
        ArgumentNullException.ThrowIfNull(hookInput);

        var details = HookEventLog.CreateDetails(("tool", hookInput.ToolName), ("source", hookInput.Source), ("stopReason", hookInput.StopReason), ("sessionEndReason", hookInput.SessionEndReason), ("notificationType", hookInput.NotificationType), ("agentName", hookInput.AgentName), ("agentDisplayName", hookInput.AgentDisplayName), ("errorContext", hookInput.ErrorContext), ("recoverable", hookInput.Recoverable?.ToString() ?? string.Empty));
        return s_eventLog.AppendReceived(configuredHookEventName, hookInput, details, IsUserPromptSubmittedEvent(configuredHookEventName));
    }

    public static TimeSpan AppendRuntimeResult(string configuredHookEventName, GitHubCopilotHookInput hookInput, string commandName, bool succeeded, bool runtimeUnavailable, int activeSessionCount, string message, string timingDetails = "")
    {
        ArgumentNullException.ThrowIfNull(hookInput);

        return s_eventLog.AppendRuntimeResult(configuredHookEventName, hookInput, commandName, succeeded, runtimeUnavailable, activeSessionCount, message, timingDetails: timingDetails);
    }

    public static string GetDefaultLogFilePath() => s_eventLog.GetDefaultLogFilePath();

    public static IReadOnlyList<string> ReadRecentLines(int maximumLineCount) => s_eventLog.ReadRecentLines(maximumLineCount);

    private static bool IsUserPromptSubmittedEvent(string hookEventName) =>
        string.Equals(hookEventName?.Trim(), GitHubCopilotHookEventNames.UserPromptSubmitted, StringComparison.Ordinal)
        || string.Equals(hookEventName?.Trim(), GitHubCopilotHookEventNames.PascalCaseUserPromptSubmittedAlias, StringComparison.Ordinal);
}
