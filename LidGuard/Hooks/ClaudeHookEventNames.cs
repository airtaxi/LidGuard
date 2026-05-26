namespace LidGuard.Hooks;

public static class ClaudeHookEventNames
{
    public const string AgentToolName = "Agent";
    public const string BashToolName = "Bash";
    public const string Elicitation = "Elicitation";
    public const string ElicitationCompleteNotificationType = "elicitation_complete";
    public const string ElicitationDialogNotificationType = "elicitation_dialog";
    public const string ElicitationResponseNotificationType = "elicitation_response";
    public const string MonitorToolName = "Monitor";
    public const string Notification = "Notification";
    public const string PermissionPromptNotificationType = "permission_prompt";
    public const string PermissionRequest = "PermissionRequest";
    public const string PostToolUse = "PostToolUse";
    public const string PostToolUseFailure = "PostToolUseFailure";
    public const string PowerShellToolName = "PowerShell";
    public const string PreToolUse = "PreToolUse";
    public const string SessionEnd = "SessionEnd";
    public const string Stop = "Stop";
    public const string StopFailure = "StopFailure";
    public const string SubagentStart = "SubagentStart";
    public const string SubagentStop = "SubagentStop";
    public const string TaskCompleted = "TaskCompleted";
    public const string TaskCreated = "TaskCreated";
    public const string TaskStopToolName = "TaskStop";
    public const string TaskToolName = "Task";
    public const string UserPromptSubmit = "UserPromptSubmit";

    public static bool IsStopTrigger(string hookEventName) => hookEventName.Equals(Stop, StringComparison.Ordinal) || hookEventName.Equals(StopFailure, StringComparison.Ordinal) || hookEventName.Equals(SessionEnd, StringComparison.Ordinal);
}
