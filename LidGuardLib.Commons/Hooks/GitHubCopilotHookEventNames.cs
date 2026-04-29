namespace LidGuardLib.Commons.Hooks;

public static class GitHubCopilotHookEventNames
{
    public const string AgentStop = "agentStop";
    public const string AskUserToolName = "ask_user";
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
    public const string PascalCaseUserPromptSubmittedAlias = "UserPromptSubmit";
    public const string PostToolUse = "postToolUse";
    public const string PermissionPromptNotificationType = "permission_prompt";
    public const string PermissionRequest = "permissionRequest";
    public const string PreToolUse = "preToolUse";
    public const string SessionEnd = "sessionEnd";
    public const string SessionStart = "sessionStart";
    public const string UserPromptSubmitted = "userPromptSubmitted";

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
