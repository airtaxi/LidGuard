namespace LidGuard.Hooks;

internal static class OpenCodeHookEventNames
{
    public const string ChatMessage = "chat.message";
    public const string PermissionAsk = "permission.ask";
    public const string PermissionAsked = "permission.asked";
    public const string PermissionReplied = "permission.replied";
    public const string QuestionAsked = "question.asked";
    public const string QuestionRejected = "question.rejected";
    public const string QuestionReplied = "question.replied";
    public const string QuestionV2Asked = "question.v2.asked";
    public const string QuestionV2Rejected = "question.v2.rejected";
    public const string QuestionV2Replied = "question.v2.replied";
    public const string SessionDeleted = "session.deleted";
    public const string SessionError = "session.error";
    public const string SessionIdle = "session.idle";
    public const string SessionStatus = "session.status";
    public const string ToolExecuteAfter = "tool.execute.after";
    public const string ToolExecuteBefore = "tool.execute.before";

    public static bool IsActivityEvent(string hookEventName) => hookEventName is ToolExecuteBefore or ToolExecuteAfter;

    public static bool IsSoftLockEvent(string hookEventName) => hookEventName is PermissionAsked or QuestionAsked or QuestionV2Asked;

    public static bool IsSoftLockClearEvent(string hookEventName) => hookEventName is PermissionReplied or QuestionRejected or QuestionReplied or QuestionV2Rejected or QuestionV2Replied;

    public static bool IsStopTrigger(string hookEventName, OpenCodeHookInput hookInput)
    {
        if (hookEventName is SessionIdle or SessionDeleted or SessionError) return true;
        return hookEventName == SessionStatus && hookInput.SessionStatus.Equals("idle", StringComparison.OrdinalIgnoreCase);
    }
}
