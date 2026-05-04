namespace LidGuard.Hooks;

public static class GitHubCopilotHookEventNames
{
    public const string AgentStop = "agentStop";
    public const string AgentCompletedNotificationType = "agent_completed";
    public const string AgentIdleNotificationType = "agent_idle";
    public const string AskUserToolName = "ask_user";
    public const string BashToolName = "bash";
    public const string ElicitationDialogNotificationType = "elicitation_dialog";
    public const string ErrorOccurred = "errorOccurred";
    public const string Notification = "notification";
    public const string NotificationPascalCaseAlias = "Notification";
    public const string PascalCaseAgentStopAlias = "Stop";
    public const string PascalCaseErrorOccurredAlias = "ErrorOccurred";
    public const string PascalCasePostToolUseAlias = "PostToolUse";
    public const string PascalCasePermissionRequestAlias = "PermissionRequest";
    public const string PascalCasePreToolUseAlias = "PreToolUse";
    public const string PascalCaseSessionEndAlias = "SessionEnd";
    public const string PascalCaseSessionStartAlias = "SessionStart";
    public const string PascalCaseSubagentStartAlias = "SubagentStart";
    public const string PascalCaseSubagentStopAlias = "SubagentStop";
    public const string PascalCaseUserPromptSubmittedAlias = "UserPromptSubmit";
    public const string PostToolUse = "postToolUse";
    public const string PowerShellToolName = "powershell";
    public const string PermissionPromptNotificationType = "permission_prompt";
    public const string PermissionRequest = "permissionRequest";
    public const string PreToolUse = "preToolUse";
    public const string ReadAgentToolName = "read_agent";
    public const string SessionEnd = "sessionEnd";
    public const string SessionStart = "sessionStart";
    public const string ShellCompletedNotificationType = "shell_completed";
    public const string ShellDetachedCompletedNotificationType = "shell_detached_completed";
    public const string SubagentStart = "subagentStart";
    public const string SubagentStop = "subagentStop";
    public const string TaskToolName = "task";
    public const string UserPromptSubmitted = "userPromptSubmitted";
    public const string WriteAgentToolName = "write_agent";

    public static string GetPascalCaseAlias(string hookEventName)
    {
        return hookEventName switch
        {
            AgentStop => PascalCaseAgentStopAlias,
            ErrorOccurred => PascalCaseErrorOccurredAlias,
            Notification => NotificationPascalCaseAlias,
            PostToolUse => PascalCasePostToolUseAlias,
            PermissionRequest => PascalCasePermissionRequestAlias,
            PreToolUse => PascalCasePreToolUseAlias,
            SessionEnd => PascalCaseSessionEndAlias,
            SessionStart => PascalCaseSessionStartAlias,
            SubagentStart => PascalCaseSubagentStartAlias,
            SubagentStop => PascalCaseSubagentStopAlias,
            UserPromptSubmitted => PascalCaseUserPromptSubmittedAlias,
            _ => string.Empty
        };
    }

    public static bool IsAgentStopEventName(string hookEventName)
    {
        return hookEventName.Equals(AgentStop, StringComparison.Ordinal)
            || hookEventName.Equals(PascalCaseAgentStopAlias, StringComparison.Ordinal);
    }
}
