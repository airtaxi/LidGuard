using LidGuard.Commands.Help;
using LidGuard.Ipc;
using LidGuard.Runtime;
using LidGuard.Settings;
using LidGuardLib.Commons.Power;
using LidGuardLib.Commons.Sessions;
using LidGuardLib.Commons.Settings;

namespace LidGuard.Commands;

internal static class LidGuardCommandConsole
{
    public static int WriteResponse(LidGuardPipeResponse response, bool includeSessions = false, bool includeSettings = false)
    {
        if (!response.Succeeded)
        {
            Console.Error.WriteLine(response.Message);
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(response.Message)) Console.WriteLine(response.Message);
        Console.WriteLine($"Active sessions: {response.ActiveSessionCount}");
        Console.WriteLine($"Lid state: {response.LidSwitchState}");
        Console.WriteLine($"Visible display monitor count: {response.VisibleDisplayMonitorCount}");

        if (includeSessions)
        {
            foreach (var session in response.Sessions)
            {
                var processText = session.WatchedProcessIdentifier > 0 ? session.WatchedProcessIdentifier.ToString() : "none";
                var providerDisplayText = AgentProviderDisplay.CreateProviderDisplayText(session.Provider, session.ProviderName);
                var startedAt = LidGuardCommandTimestampFormatter.FormatDisplayTimestamp(session.StartedAt);
                var lastActivityAt = LidGuardCommandTimestampFormatter.FormatDisplayTimestamp(session.LastActivityAt);
                Console.WriteLine(
                    $"- {providerDisplayText}:{session.SessionIdentifier} process={processText} softLock={DescribeSoftLockStatus(session)} cwd=\"{session.WorkingDirectory}\" started={startedAt} lastActivity={lastActivityAt}");
            }
        }

        if (includeSettings) WriteSettings(response.Settings);

        return 0;
    }

    public static void WriteSettings(LidGuardSettings settings)
    {
        var normalizedSettings = LidGuardSettings.Normalize(settings);
        var powerRequest = normalizedSettings.PowerRequest ?? PowerRequestOptions.Default;
        Console.WriteLine("Settings:");
        Console.WriteLine($"  Prevent system sleep: {powerRequest.PreventSystemSleep}");
        Console.WriteLine($"  Prevent away mode sleep: {powerRequest.PreventAwayModeSleep}");
        Console.WriteLine($"  Prevent display sleep: {powerRequest.PreventDisplaySleep}");
        Console.WriteLine($"  Change lid action: {normalizedSettings.ChangeLidAction}");
        Console.WriteLine($"  Watch parent process: {normalizedSettings.WatchParentProcess}");
        Console.WriteLine($"  Session timeout: {SessionTimeoutConfiguration.GetDisplayValue(normalizedSettings.SessionTimeoutMinutes)}");
        Console.WriteLine($"  Server runtime cleanup delay: {ServerRuntimeCleanupConfiguration.GetDisplayValue(normalizedSettings.ServerRuntimeCleanupDelayMinutes)}");
        Console.WriteLine($"  Emergency hibernation on high temperature: {normalizedSettings.EmergencyHibernationOnHighTemperature}");
        Console.WriteLine($"  Emergency hibernation temperature mode: {normalizedSettings.EmergencyHibernationTemperatureMode}");
        Console.WriteLine($"  Emergency hibernation temperature Celsius: {normalizedSettings.EmergencyHibernationTemperatureCelsius}");
        Console.WriteLine($"  Suspend mode: {normalizedSettings.SuspendMode}");
        Console.WriteLine($"  Post-stop suspend delay seconds: {normalizedSettings.PostStopSuspendDelaySeconds}");
        Console.WriteLine($"  Post-stop suspend sound: {PostStopSuspendSoundConfiguration.GetDisplayValue(normalizedSettings.PostStopSuspendSound)}");
        Console.WriteLine($"  Post-stop suspend sound volume override percent: {PostStopSuspendSoundConfiguration.GetVolumeOverrideDisplayValue(normalizedSettings.PostStopSuspendSoundVolumeOverridePercent)}");
        Console.WriteLine($"  Suspend history count: {SuspendHistoryConfiguration.GetDisplayValue(normalizedSettings.SuspendHistoryEntryCount)}");
        Console.WriteLine($"  Pre-suspend webhook URL: {PreSuspendWebhookConfiguration.GetDisplayValue(normalizedSettings.PreSuspendWebhookUrl)}");
        Console.WriteLine($"  Closed lid permission request decision: {normalizedSettings.ClosedLidPermissionRequestDecision}");
        Console.WriteLine($"  Reason: {powerRequest.Reason}");
    }

    public static int WriteHelp(int exitCode)
    {
        var helpDocument = CreateHelpDocument();
        foreach (var helpSection in LidGuardHelpContent.CreateSummarySections(helpDocument)) WriteHelpSection(helpSection);
        Console.WriteLine("Use command-specific help for full options, notes, and examples.");
        return exitCode;
    }

    public static int WriteHelpForCommand(string commandName)
    {
        if (TryWriteHelpForCommand(commandName, out var exitCode)) return exitCode;
        return WriteUnknownCommand(commandName);
    }

    public static bool TryWriteHelpForCommand(string commandName, out int exitCode)
    {
        var helpDocument = CreateHelpDocument();
        if (!LidGuardHelpContent.TryFindCommand(helpDocument, commandName, out var commandEntry))
        {
            exitCode = 1;
            return false;
        }

        foreach (var helpSection in LidGuardHelpContent.CreateCommandSections(helpDocument, commandEntry)) WriteHelpSection(helpSection);

        exitCode = 0;
        return true;
    }

    public static string GetCommandDisplayName()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath)) return "lidguard";

        var fileName = Path.GetFileNameWithoutExtension(processPath);
        return string.IsNullOrWhiteSpace(fileName) ? "lidguard" : fileName;
    }

    public static int WriteUnknownCommand(string commandName)
    {
        Console.Error.WriteLine($"Unknown command: {commandName}");
        return WriteHelp(1);
    }

    private static LidGuardHelpDocument CreateHelpDocument()
        => LidGuardHelpContent.CreateDocument(
            GetCommandDisplayName(),
            LidGuardSettingsStore.GetDefaultSettingsFilePath(),
            LidGuardRuntimeSessionLogStore.GetDefaultLogFilePath(),
            SuspendHistoryLogStore.GetDefaultLogFilePath(),
            LidGuardSupportedSystemSounds.Describe());

    private static void WriteHelpSection(LidGuardHelpSection helpSection)
    {
        Console.WriteLine($"{helpSection.Title}:");
        foreach (var detail in helpSection.Details) Console.WriteLine($"  {detail}");

        for (var commandIndex = 0; commandIndex < helpSection.Commands.Count; commandIndex++)
        {
            if (helpSection.Details.Count > 0 || commandIndex > 0) Console.WriteLine();
            WriteHelpCommand(helpSection.Commands[commandIndex]);
        }

        Console.WriteLine();
    }

    private static void WriteHelpCommand(LidGuardHelpCommand helpCommand)
    {
        Console.WriteLine($"  {helpCommand.Synopsis}");
        Console.WriteLine($"    {helpCommand.Description}");
        foreach (var helpOption in helpCommand.Options) Console.WriteLine($"    {helpOption.Label}: {helpOption.Description}");
        foreach (var note in helpCommand.Notes) Console.WriteLine($"    Note: {note}");
    }

    private static string DescribeSoftLockStatus(LidGuardSessionStatus session)
    {
        if (session.SoftLockState != LidGuardSessionSoftLockState.SoftLocked) return session.SoftLockState.ToString();

        var details = session.SoftLockState.ToString();
        if (!string.IsNullOrWhiteSpace(session.SoftLockReason)) details = $"{details}:{session.SoftLockReason}";
        if (session.SoftLockedAt is not null) details = $"{details}@{LidGuardCommandTimestampFormatter.FormatDisplayTimestamp(session.SoftLockedAt.Value)}";
        return details;
    }
}
