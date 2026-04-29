namespace LidGuardLib.Commons.Hooks;

public static class ClaudeSoftLockSignalSource
{
    public const string NotificationMatcher = "permission_prompt|elicitation_dialog|elicitation_complete|elicitation_response";
    private const string AskUserQuestionToolName = "AskUserQuestion";

    public static bool IsActivityEvent(ClaudeHookInput hookInput)
    {
        ArgumentNullException.ThrowIfNull(hookInput);

        var hookEventName = hookInput.HookEventName.Trim();
        if (hookEventName.Equals(ClaudeHookEventNames.Notification, StringComparison.Ordinal))
            return IsResolutionNotificationType(hookInput.NotificationType);

        if (hookEventName.Equals(ClaudeHookEventNames.PreToolUse, StringComparison.Ordinal)
            || hookEventName.Equals(ClaudeHookEventNames.PostToolUse, StringComparison.Ordinal)
            || hookEventName.Equals(ClaudeHookEventNames.PostToolUseFailure, StringComparison.Ordinal))
            return IsActivityToolName(hookInput.ToolName);

        return false;
    }

    public static bool TryGetSoftLockReason(ClaudeHookInput hookInput, out string softLockReason)
    {
        ArgumentNullException.ThrowIfNull(hookInput);

        softLockReason = string.Empty;
        if (!hookInput.HookEventName.Trim().Equals(ClaudeHookEventNames.Notification, StringComparison.Ordinal)) return false;

        var notificationType = hookInput.NotificationType.Trim();
        if (!notificationType.Equals(ClaudeHookEventNames.PermissionPromptNotificationType, StringComparison.Ordinal)
            && !notificationType.Equals(ClaudeHookEventNames.ElicitationDialogNotificationType, StringComparison.Ordinal))
            return false;

        softLockReason = notificationType;
        return true;
    }

    private static bool IsActivityToolName(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName)) return false;
        return !toolName.Trim().Equals(AskUserQuestionToolName, StringComparison.Ordinal);
    }

    private static bool IsResolutionNotificationType(string notificationType)
    {
        var normalizedNotificationType = notificationType.Trim();
        return normalizedNotificationType.Equals(ClaudeHookEventNames.ElicitationCompleteNotificationType, StringComparison.Ordinal)
            || normalizedNotificationType.Equals(ClaudeHookEventNames.ElicitationResponseNotificationType, StringComparison.Ordinal);
    }
}
