namespace LidGuard.Hooks;

internal static class OpenCodeHookEventNames
{
    public const string ChatMessage = "chat.message";
    public const string FormCancelled = "form.cancelled";
    public const string FormCreated = "form.created";
    public const string FormReplied = "form.replied";
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
    public const string SessionExecutionFailed = "session.execution.failed";
    public const string SessionExecutionInterrupted = "session.execution.interrupted";
    public const string SessionExecutionSucceeded = "session.execution.succeeded";
    public const string SessionIdle = "session.idle";
    public const string SessionStatus = "session.status";
    public const string ToolExecuteAfter = "tool.execute.after";
    public const string ToolExecuteBefore = "tool.execute.before";

    public static bool IsActivityEvent(string hookEventName) => hookEventName is ToolExecuteBefore or ToolExecuteAfter;

    public static bool IsSoftLockEvent(string hookEventName) => hookEventName is FormCreated or PermissionAsked or QuestionAsked or QuestionV2Asked;

    public static bool IsSoftLockClearEvent(string hookEventName) => hookEventName is FormCancelled or FormReplied or PermissionReplied or QuestionRejected or QuestionReplied or QuestionV2Rejected or QuestionV2Replied;

    public static bool IsStopTrigger(string hookEventName, OpenCodeHookInput hookInput)
    {
        if (hookEventName is SessionDeleted or SessionError or SessionExecutionFailed or SessionExecutionInterrupted or SessionExecutionSucceeded or SessionIdle) return true;
        return hookEventName == SessionStatus && hookInput.SessionStatus.Equals("idle", StringComparison.OrdinalIgnoreCase);
    }
}
