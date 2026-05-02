using LidGuard.Ipc;
using LidGuardLib.Commons.Settings;
using LidGuardLib.Commons.Sessions;

namespace LidGuard.Runtime;

internal static class LidGuardRuntimeLogWriter
{
    private const string EmergencyHibernationMonitorCommandName = "emergency-hibernation-monitor";

    public static void AppendRuntimeLog(string eventName, string command, LidGuardPipeResponse response)
    {
        LidGuardRuntimeSessionLogStore.Append(new LidGuardRuntimeSessionLogEntry
        {
            EventName = eventName,
            Command = command,
            Succeeded = response.Succeeded,
            Message = response.Message,
            ActiveSessionCount = response.ActiveSessionCount
        });
    }

    public static void AppendEmergencyHibernationLog(
        string eventName,
        LidGuardPipeResponse response,
        int observedTemperatureCelsius,
        int emergencyHibernationTemperatureCelsius,
        EmergencyHibernationTemperatureMode emergencyHibernationTemperatureMode)
    {
        LidGuardRuntimeSessionLogStore.Append(new LidGuardRuntimeSessionLogEntry
        {
            EventName = eventName,
            Command = EmergencyHibernationMonitorCommandName,
            Succeeded = response.Succeeded,
            Message = $"{response.Message} Observed temperature: {DescribeEmergencyHibernationTemperature(observedTemperatureCelsius, emergencyHibernationTemperatureCelsius, emergencyHibernationTemperatureMode)}.",
            ActiveSessionCount = response.ActiveSessionCount
        });
    }

    public static void AppendSessionLog(string eventName, LidGuardPipeRequest request, LidGuardPipeResponse response, int watchedProcessIdentifier = 0)
    {
        LidGuardRuntimeSessionLogStore.Append(new LidGuardRuntimeSessionLogEntry
        {
            EventName = eventName,
            Command = request.Command,
            Provider = request.Provider,
            ProviderName = AgentProviderDisplay.NormalizeProviderName(request.Provider, request.ProviderName),
            SessionIdentifier = request.SessionIdentifier,
            IsProviderSessionEnd = request.IsProviderSessionEnd,
            SessionEndReason = request.SessionEndReason,
            SoftLockState = request.Command == LidGuardPipeCommands.MarkSessionSoftLocked ? LidGuardSessionSoftLockState.SoftLocked : LidGuardSessionSoftLockState.None,
            SoftLockReason = request.SessionStateReason,
            WatchedProcessIdentifier = watchedProcessIdentifier > 0 ? watchedProcessIdentifier : request.WatchedProcessIdentifier,
            WorkingDirectory = request.WorkingDirectory,
            TranscriptPath = request.TranscriptPath,
            Succeeded = response.Succeeded,
            Message = response.Message,
            ActiveSessionCount = response.ActiveSessionCount
        });
    }

    public static void AppendSessionLog(string eventName, LidGuardPipeRequest request, LidGuardPipeResponse response, LidGuardSessionSnapshot snapshot)
    {
        LidGuardRuntimeSessionLogStore.Append(new LidGuardRuntimeSessionLogEntry
        {
            EventName = eventName,
            Command = request.Command,
            Provider = request.Provider,
            ProviderName = snapshot.ProviderName,
            SessionIdentifier = request.SessionIdentifier,
            IsProviderSessionEnd = request.IsProviderSessionEnd,
            SessionEndReason = request.SessionEndReason,
            SoftLockState = snapshot.SoftLockState,
            SoftLockReason = snapshot.SoftLockReason,
            SoftLockedAt = snapshot.SoftLockedAt,
            WatchedProcessIdentifier = snapshot.WatchedProcessIdentifier,
            WorkingDirectory = snapshot.WorkingDirectory,
            TranscriptPath = snapshot.TranscriptPath,
            Succeeded = response.Succeeded,
            Message = response.Message,
            ActiveSessionCount = response.ActiveSessionCount
        });
    }

    public static void AppendSessionLog(string eventName, LidGuardSessionStopRequest request, LidGuardPipeResponse response, string commandName)
    {
        LidGuardRuntimeSessionLogStore.Append(new LidGuardRuntimeSessionLogEntry
        {
            EventName = eventName,
            Command = commandName,
            Provider = request.Provider,
            ProviderName = AgentProviderDisplay.NormalizeProviderName(request.Provider, request.ProviderName),
            SessionIdentifier = request.SessionIdentifier,
            IsProviderSessionEnd = request.IsProviderSessionEnd,
            SessionEndReason = request.SessionEndReason,
            Succeeded = response.Succeeded,
            Message = response.Message,
            ActiveSessionCount = response.ActiveSessionCount
        });
    }

    public static void AppendSessionLog(
        string eventName,
        LidGuardSessionStopRequest request,
        LidGuardPipeResponse response,
        LidGuardSessionSnapshot snapshot,
        string commandName)
    {
        LidGuardRuntimeSessionLogStore.Append(new LidGuardRuntimeSessionLogEntry
        {
            EventName = eventName,
            Command = commandName,
            Provider = request.Provider,
            ProviderName = snapshot.ProviderName,
            SessionIdentifier = request.SessionIdentifier,
            IsProviderSessionEnd = request.IsProviderSessionEnd,
            SessionEndReason = request.SessionEndReason,
            SoftLockState = snapshot.SoftLockState,
            SoftLockReason = snapshot.SoftLockReason,
            SoftLockedAt = snapshot.SoftLockedAt,
            WatchedProcessIdentifier = snapshot.WatchedProcessIdentifier,
            WorkingDirectory = snapshot.WorkingDirectory,
            TranscriptPath = snapshot.TranscriptPath,
            Succeeded = response.Succeeded,
            Message = response.Message,
            ActiveSessionCount = response.ActiveSessionCount
        });
    }

    public static void AppendSessionLog(
        string eventName,
        PendingSuspendContext pendingSuspendContext,
        LidGuardPipeResponse response,
        LidGuardSessionSnapshot snapshot)
    {
        LidGuardRuntimeSessionLogStore.Append(new LidGuardRuntimeSessionLogEntry
        {
            EventName = eventName,
            Command = pendingSuspendContext.CommandName,
            Provider = pendingSuspendContext.Provider,
            ProviderName = pendingSuspendContext.ProviderName,
            SessionIdentifier = pendingSuspendContext.SessionIdentifier,
            SoftLockState = snapshot.SoftLockState,
            SoftLockReason = snapshot.SoftLockReason,
            SoftLockedAt = snapshot.SoftLockedAt,
            WatchedProcessIdentifier = snapshot.WatchedProcessIdentifier,
            WorkingDirectory = pendingSuspendContext.WorkingDirectory,
            TranscriptPath = snapshot.TranscriptPath,
            Succeeded = response.Succeeded,
            Message = response.Message,
            ActiveSessionCount = response.ActiveSessionCount
        });
    }

    private static string DescribeEmergencyHibernationTemperature(
        int observedTemperatureCelsius,
        int emergencyHibernationTemperatureCelsius,
        EmergencyHibernationTemperatureMode emergencyHibernationTemperatureMode)
        => $"{observedTemperatureCelsius} Celsius using {emergencyHibernationTemperatureMode} mode (threshold: {emergencyHibernationTemperatureCelsius} Celsius)";
}
