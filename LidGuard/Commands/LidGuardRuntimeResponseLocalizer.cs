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
            LidGuardPipeResponseMessageCodes.CleanupOrphansCompleted => Format("RuntimeResponseCleanupOrphansCompleted", Argument(messageArguments, 0)),
            LidGuardPipeResponseMessageCodes.FailedToStartRuntime => Get("RuntimeResponseFailedToStartRuntime"),
            LidGuardPipeResponseMessageCodes.RuntimeIsRunning => Get("RuntimeResponseRuntimeIsRunning"),
            LidGuardPipeResponseMessageCodes.RuntimeNotRunning => LocalizationService.GetString("ConsoleRuntimeNotRunning"),
            LidGuardPipeResponseMessageCodes.SettingsRuntimeUpdated => LocalizationService.GetString("SettingsRuntimeUpdated"),
            LidGuardPipeResponseMessageCodes.SessionAlreadyStopped => Format("RuntimeResponseSessionAlreadyStopped", Argument(messageArguments, 0)),
            LidGuardPipeResponseMessageCodes.SessionIdAlreadyStopped => Format("RuntimeResponseSessionIdAlreadyStopped", Argument(messageArguments, 0)),
            LidGuardPipeResponseMessageCodes.SessionIdAlreadyStoppedForProvider => Format("RuntimeResponseSessionIdAlreadyStoppedForProvider", Argument(messageArguments, 0), Argument(messageArguments, 1)),
            LidGuardPipeResponseMessageCodes.SessionRemoved => Format("RuntimeResponseSessionRemoved", Argument(messageArguments, 0)),
            LidGuardPipeResponseMessageCodes.SessionRemovedAll => Format("RuntimeResponseSessionRemovedAll", Argument(messageArguments, 0)),
            LidGuardPipeResponseMessageCodes.SessionRemovedMatchingProviderSessionId => Format("RuntimeResponseSessionRemovedMatchingProviderSessionId", Argument(messageArguments, 0), Argument(messageArguments, 1), Argument(messageArguments, 2)),
            LidGuardPipeResponseMessageCodes.SessionRemovedMatchingSessionId => Format("RuntimeResponseSessionRemovedMatchingSessionId", Argument(messageArguments, 0), Argument(messageArguments, 1)),
            LidGuardPipeResponseMessageCodes.SessionRemoveNoActiveSessions => Get("RuntimeResponseSessionRemoveNoActiveSessions"),
            LidGuardPipeResponseMessageCodes.SessionStarted => LocalizeSessionStarted(messageArguments),
            LidGuardPipeResponseMessageCodes.SessionStopped => Format("RuntimeResponseSessionStopped", Argument(messageArguments, 0)),
            LidGuardPipeResponseMessageCodes.WatchedProcessExited => Format("RuntimeResponseWatchedProcessExited", Argument(messageArguments, 0)),
            LidGuardPipeResponseMessageCodes.WatchedProcessOrphanCleaned => Format("RuntimeResponseWatchedProcessOrphanCleaned", Argument(messageArguments, 0)),
            _ => fallbackMessage
        };
    }

    private static string LocalizeSessionStarted(string[] messageArguments)
    {
        var sessionKey = Argument(messageArguments, 0);
        var watcherStatusKind = Argument(messageArguments, 1);
        var processIdentifier = Argument(messageArguments, 2);
        return watcherStatusKind switch
        {
            LidGuardPipeResponseMessageCodes.WatcherStatusWatchedProcess => Format("RuntimeResponseSessionStartedWatchedProcess", sessionKey, processIdentifier),
            _ => Format("RuntimeResponseSessionStartedNoWatchedProcess", sessionKey)
        };
    }

    private static string CreateSuspendScheduleMessage(LidGuardPipeResponse response)
    {
        var suspendMode = LocalizationService.DisplaySuspendMode(response.SuspendMode);
        var suspendDelay = response.SuspendDelaySeconds == 0
            ? Get("RuntimeResponseSuspendDelayImmediate")
            : Format("RuntimeResponseSuspendDelaySeconds", response.SuspendDelaySeconds);
        var suspendReason = response.SuspendReasonCode == LidGuardPipeResponseMessageCodes.SuspendReasonSoftLocked
            ? Get("RuntimeResponseSuspendReasonSoftLocked")
            : Get("RuntimeResponseSuspendReasonCompleted");
        return Format("RuntimeResponseSuspendScheduled", suspendMode, suspendDelay, suspendReason);
    }

    private static string Argument(string[] messageArguments, int argumentIndex)
    {
        if (messageArguments is null || argumentIndex < 0 || argumentIndex >= messageArguments.Length) return string.Empty;
        return messageArguments[argumentIndex] ?? string.Empty;
    }

    private static string Get(string resourceName)
        => LocalizationService.GetString(resourceName);

    private static string Format(string resourceName, params object[] arguments)
        => string.Format(CultureInfo.CurrentCulture, Get(resourceName), arguments);
}
