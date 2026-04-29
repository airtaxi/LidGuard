namespace LidGuardLib.Commons.Hooks;

public static class GitHubCopilotSoftLockSignalSource
{
    public static bool IsActivityEvent(string configuredHookEventName, GitHubCopilotHookInput hookInput)
    {
        ArgumentNullException.ThrowIfNull(hookInput);

        if (configuredHookEventName.Equals(GitHubCopilotHookEventNames.PreToolUse, StringComparison.Ordinal)
            || configuredHookEventName.Equals(GitHubCopilotHookEventNames.PostToolUse, StringComparison.Ordinal))
            return IsActivityToolName(hookInput.ToolName);

        return false;
    }

    public static bool TryGetSoftLockReason(string configuredHookEventName, GitHubCopilotHookInput hookInput, out string softLockReason)
    {
        ArgumentNullException.ThrowIfNull(hookInput);

        softLockReason = string.Empty;
        if (!configuredHookEventName.Equals(GitHubCopilotHookEventNames.Notification, StringComparison.Ordinal)) return false;

        var notificationType = hookInput.NotificationType.Trim();
        if (!notificationType.Equals(GitHubCopilotHookEventNames.PermissionPromptNotificationType, StringComparison.Ordinal)
            && !notificationType.Equals(GitHubCopilotHookEventNames.ElicitationDialogNotificationType, StringComparison.Ordinal))
            return false;

        softLockReason = notificationType;
        return true;
    }

    private static bool IsActivityToolName(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName)) return false;
        return !toolName.Trim().Equals(GitHubCopilotHookEventNames.AskUserToolName, StringComparison.OrdinalIgnoreCase);
    }
}
