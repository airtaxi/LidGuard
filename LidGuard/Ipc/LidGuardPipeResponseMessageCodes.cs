namespace LidGuard.Ipc;

internal static class LidGuardPipeResponseMessageCodes
{
    public const string CleanupOrphansCompleted = "cleanup-orphans-completed";
    public const string FailedToStartRuntime = "failed-to-start-runtime";
    public const string RuntimeIsRunning = "runtime-is-running";
    public const string RuntimeNotRunning = "runtime-not-running";
    public const string SettingsRuntimeUpdated = "settings-runtime-updated";
    public const string SessionAlreadyStopped = "session-already-stopped";
    public const string SessionIdAlreadyStopped = "session-id-already-stopped";
    public const string SessionIdAlreadyStoppedForProvider = "session-id-already-stopped-for-provider";
    public const string SessionRemoved = "session-removed";
    public const string SessionRemovedAll = "session-removed-all";
    public const string SessionRemovedMatchingProviderSessionId = "session-removed-matching-provider-session-id";
    public const string SessionRemovedMatchingSessionId = "session-removed-matching-session-id";
    public const string SessionRemoveNoActiveSessions = "session-remove-no-active-sessions";
    public const string SessionStarted = "session-started";
    public const string SessionStopped = "session-stopped";
    public const string WatchedCodexWorkingDirectoryAlreadyStopped = "watched-codex-working-directory-already-stopped";
    public const string WatchedCodexWorkingDirectoryCleaned = "watched-codex-working-directory-cleaned";
    public const string WatchedCodexWorkingDirectorySessionCleaned = "watched-codex-working-directory-session-cleaned";
    public const string WatchedProcessExited = "watched-process-exited";
    public const string WatchedProcessOrphanCleaned = "watched-process-orphan-cleaned";

    public const string WatcherStatusCodexShellHostFallback = "codex-shell-host-fallback";
    public const string WatcherStatusCodexShellHostFallbackSkipped = "codex-shell-host-fallback-skipped";
    public const string WatcherStatusNone = "none";
    public const string WatcherStatusWatchedProcess = "watched-process";

    public const string SuspendReasonCompleted = "completed";
    public const string SuspendReasonSoftLocked = "soft-locked";
}
