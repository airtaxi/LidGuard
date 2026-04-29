namespace LidGuardLib.Commons.Hooks;

public static class ClaudeHookEventNames
{
    public const string PermissionDenied = "PermissionDenied";
    public const string PermissionRequest = "PermissionRequest";
    public const string SessionEnd = "SessionEnd";
    public const string Stop = "Stop";
    public const string StopFailure = "StopFailure";
    public const string UserPromptSubmit = "UserPromptSubmit";

    public static bool IsStopTrigger(string hookEventName) =>
        hookEventName.Equals(Stop, StringComparison.Ordinal)
        || hookEventName.Equals(StopFailure, StringComparison.Ordinal)
        || hookEventName.Equals(PermissionRequest, StringComparison.Ordinal)
        || hookEventName.Equals(PermissionDenied, StringComparison.Ordinal)
        || hookEventName.Equals(SessionEnd, StringComparison.Ordinal);
}
