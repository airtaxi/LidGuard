using System.Globalization;
using LidGuard.Ipc;
using LidGuard.Localization;

namespace LidGuard.Commands;

internal static class LidGuardRuntimeResponseLocalizer
{
    public static string Localize(LidGuardPipeResponse response)
    {
        var localizedMessage = Localize(response.MessageCode, response.MessageArguments, response.Message);
        if (!response.SuspendScheduled) return localizedMessage;

        var suspendScheduleMessage = CreateSuspendScheduleMessage(response);
        if (string.IsNullOrWhiteSpace(localizedMessage)) return suspendScheduleMessage;
        return $"{localizedMessage} {suspendScheduleMessage}";
    }

    public static string Localize(string messageCode, string[] messageArguments, string fallbackMessage)
    {
        if (string.IsNullOrWhiteSpace(messageCode)) return fallbackMessage;

        return messageCode switch
        {
            LidGuardPipeResponseMessageCodes.CleanupOrphansCompleted => Format("RuntimeResponseCleanupOrphansCompleted", "Cleaned {0} orphan session(s).", Argument(messageArguments, 0)),
            LidGuardPipeResponseMessageCodes.FailedToStartRuntime => Get("RuntimeResponseFailedToStartRuntime", "Failed to start the LidGuard runtime."),
            LidGuardPipeResponseMessageCodes.RuntimeIsRunning => Get("RuntimeResponseRuntimeIsRunning", "LidGuard runtime is running."),
            LidGuardPipeResponseMessageCodes.RuntimeNotRunning => LidGuardText.ConsoleRuntimeNotRunning,
            LidGuardPipeResponseMessageCodes.SettingsRuntimeUpdated => LidGuardText.SettingsRuntimeUpdated,
            LidGuardPipeResponseMessageCodes.SessionAlreadyStopped => Format("RuntimeResponseSessionAlreadyStopped", "Session {0} is already stopped.", Argument(messageArguments, 0)),
            LidGuardPipeResponseMessageCodes.SessionIdAlreadyStopped => Format("RuntimeResponseSessionIdAlreadyStopped", "Session id {0} is already stopped.", Argument(messageArguments, 0)),
            LidGuardPipeResponseMessageCodes.SessionIdAlreadyStoppedForProvider => Format("RuntimeResponseSessionIdAlreadyStoppedForProvider", "Session id {0} is already stopped for {1}.", Argument(messageArguments, 0), Argument(messageArguments, 1)),
            LidGuardPipeResponseMessageCodes.SessionRemoved => Format("RuntimeResponseSessionRemoved", "Removed {0}.", Argument(messageArguments, 0)),
            LidGuardPipeResponseMessageCodes.SessionRemovedAll => Format("RuntimeResponseSessionRemovedAll", "Removed all {0} active session(s).", Argument(messageArguments, 0)),
            LidGuardPipeResponseMessageCodes.SessionRemovedMatchingProviderSessionId => Format("RuntimeResponseSessionRemovedMatchingProviderSessionId", "Removed {0} session(s) matching {1} session id \"{2}\".", Argument(messageArguments, 0), Argument(messageArguments, 1), Argument(messageArguments, 2)),
            LidGuardPipeResponseMessageCodes.SessionRemovedMatchingSessionId => Format("RuntimeResponseSessionRemovedMatchingSessionId", "Removed {0} session(s) matching session id \"{1}\".", Argument(messageArguments, 0), Argument(messageArguments, 1)),
            LidGuardPipeResponseMessageCodes.SessionRemoveNoActiveSessions => Get("RuntimeResponseSessionRemoveNoActiveSessions", "There are no active sessions to remove."),
            LidGuardPipeResponseMessageCodes.SessionStarted => LocalizeSessionStarted(messageArguments),
            LidGuardPipeResponseMessageCodes.SessionStopped => Format("RuntimeResponseSessionStopped", "Stopped {0}.", Argument(messageArguments, 0)),
            LidGuardPipeResponseMessageCodes.WatchedCodexWorkingDirectoryAlreadyStopped => Format("RuntimeResponseWatchedCodexWorkingDirectoryAlreadyStopped", "Watched Codex working directory \"{0}\" is already stopped.", Argument(messageArguments, 0)),
            LidGuardPipeResponseMessageCodes.WatchedCodexWorkingDirectoryCleaned => Format("RuntimeResponseWatchedCodexWorkingDirectoryCleaned", "Cleaned {0} watched Codex session(s) for working directory \"{1}\" and left process=none Codex sessions untouched.", Argument(messageArguments, 0), Argument(messageArguments, 1)),
            LidGuardPipeResponseMessageCodes.WatchedCodexWorkingDirectorySessionCleaned => Format("RuntimeResponseWatchedCodexWorkingDirectorySessionCleaned", "Cleaned watched Codex session {0} for working directory \"{1}\".", Argument(messageArguments, 0), Argument(messageArguments, 1)),
            LidGuardPipeResponseMessageCodes.WatchedProcessExited => Format("RuntimeResponseWatchedProcessExited", "Watched process exited for {0}.", Argument(messageArguments, 0)),
            LidGuardPipeResponseMessageCodes.WatchedProcessOrphanCleaned => Format("RuntimeResponseWatchedProcessOrphanCleaned", "Cleaned orphan session {0}.", Argument(messageArguments, 0)),
            _ => fallbackMessage
        };
    }

    private static string LocalizeSessionStarted(string[] messageArguments)
    {
        var sessionKey = Argument(messageArguments, 0);
        var watcherStatusKind = Argument(messageArguments, 1);
        var processIdentifier = Argument(messageArguments, 2);
        var codexShellHostDescription = Argument(messageArguments, 3);
        return watcherStatusKind switch
        {
            LidGuardPipeResponseMessageCodes.WatcherStatusCodexShellHostFallback => Format("RuntimeResponseSessionStartedCodexShellHostFallback", "Started {0}. Watching process {1} through Codex shell-host fallback.", sessionKey, processIdentifier),
            LidGuardPipeResponseMessageCodes.WatcherStatusWatchedProcess => Format("RuntimeResponseSessionStartedWatchedProcess", "Started {0}. Watching process {1}.", sessionKey, processIdentifier),
            LidGuardPipeResponseMessageCodes.WatcherStatusCodexShellHostFallbackSkipped => Format("RuntimeResponseSessionStartedCodexShellHostFallbackSkipped", "Started {0}. Codex fallback watchdog only attaches when the resolved Codex process or its direct parent is {1}; a stop hook is required.", sessionKey, codexShellHostDescription),
            _ => Format("RuntimeResponseSessionStartedNoWatchedProcess", "Started {0}. No watched process was resolved; a stop hook is required.", sessionKey)
        };
    }

    private static string CreateSuspendScheduleMessage(LidGuardPipeResponse response)
    {
        var suspendMode = LidGuardText.DisplaySuspendMode(response.SuspendMode);
        var suspendDelay = response.SuspendDelaySeconds == 0
            ? Get("RuntimeResponseSuspendDelayImmediate", "immediately")
            : Format("RuntimeResponseSuspendDelaySeconds", "in {0} second(s)", response.SuspendDelaySeconds);
        var suspendReason = response.SuspendReasonCode == LidGuardPipeResponseMessageCodes.SuspendReasonSoftLocked
            ? Get("RuntimeResponseSuspendReasonSoftLocked", "because the lid is closed, no suspend-blocking visible display monitors remain, and all remaining sessions are soft-locked.")
            : Get("RuntimeResponseSuspendReasonCompleted", "because the lid is closed, no suspend-blocking visible display monitors remain, and the last session stopped.");
        return Format("RuntimeResponseSuspendScheduled", "Scheduled {0} {1} {2}", suspendMode, suspendDelay, suspendReason);
    }

    private static string Argument(string[] messageArguments, int argumentIndex)
    {
        if (messageArguments is null || argumentIndex < 0 || argumentIndex >= messageArguments.Length) return string.Empty;
        return messageArguments[argumentIndex] ?? string.Empty;
    }

    private static string Get(string resourceName, string fallbackValue)
        => LidGuardText.GetResourceString(resourceName, fallbackValue);

    private static string Format(string resourceName, string fallbackValue, params object[] arguments)
        => string.Format(CultureInfo.CurrentCulture, Get(resourceName, fallbackValue), arguments);
}
