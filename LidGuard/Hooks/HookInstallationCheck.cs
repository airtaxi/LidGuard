namespace LidGuard.Hooks;

public enum HookInstallationCheck
{
    HooksFeatureFlag = 0,
    HooksObject = 1,
    ManagedBlock = 2,
    ManagedHookEntries = 3,
    UserPromptSubmitHook = 4,
    UserPromptSubmittedHook = 5,
    PreToolUseHook = 6,
    PostToolUseHook = 7,
    PostToolUseFailureHook = 8,
    SubagentStartHook = 9,
    SubagentStopHook = 10,
    TaskCreatedHook = 11,
    TaskCompletedHook = 12,
    StopHook = 13,
    StopFailureHook = 14,
    ElicitationHook = 15,
    PermissionRequestHook = 16,
    NotificationHook = 17,
    SessionStartHook = 18,
    SessionEndHook = 19,
    AgentStopHook = 20,
    ErrorOccurredHook = 21,
    ExpectedHookCommand = 22,
    ExpectedHookCommands = 23,
    ExpectedNotificationMatcher = 24,
    ExpectedHookShell = 25,
    ValidHookCommand = 26,
    ConflictingAgentStopHooks = 27
}
