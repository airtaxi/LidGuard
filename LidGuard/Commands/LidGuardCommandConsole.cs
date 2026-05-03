using LidGuard.Commands.Help;
using LidGuard.Ipc;
using LidGuard.Localization;
using LidGuard.Runtime;
using LidGuard.Settings;
using LidGuard.Power;
using LidGuard.Sessions;

namespace LidGuard.Commands;

internal static class LidGuardCommandConsole
{
    public static int WriteResponse(LidGuardPipeResponse response, bool includeSessions = false, bool includeSettings = false)
    {
        if (!response.Succeeded)
        {
            Console.Error.WriteLine(LidGuardRuntimeResponseLocalizer.Localize(response));
            return 1;
        }

        var responseMessage = LidGuardRuntimeResponseLocalizer.Localize(response);
        if (!string.IsNullOrWhiteSpace(responseMessage)) Console.WriteLine(responseMessage);
        Console.WriteLine(LidGuardText.ConsoleActiveSessions(response.ActiveSessionCount));
        Console.WriteLine(LidGuardText.ConsoleLidState(LidGuardText.DisplayLidSwitchState(response.LidSwitchState)));
        Console.WriteLine(LidGuardText.ConsoleVisibleDisplayMonitorCount(response.VisibleDisplayMonitorCount));

        if (includeSessions)
        {
            foreach (var session in response.Sessions)
            {
                var processText = session.WatchedProcessIdentifier > 0 ? session.WatchedProcessIdentifier.ToString() : LidGuardText.SessionProcessNone;
                var providerDisplayText = AgentProviderDisplay.CreateProviderDisplayText(session.Provider, session.ProviderName);
                var startedAt = LidGuardCommandTimestampFormatter.FormatDisplayTimestamp(session.StartedAt);
                var lastActivityAt = LidGuardCommandTimestampFormatter.FormatDisplayTimestamp(session.LastActivityAt);
                Console.WriteLine(
                    LidGuardText.ConsoleSessionLine(
                        providerDisplayText,
                        session.SessionIdentifier,
                        processText,
                        DescribeSoftLockStatus(session),
                        session.WorkingDirectory,
                        startedAt,
                        lastActivityAt));
            }
        }

        if (includeSettings) WriteSettings(response.Settings);

        return 0;
    }

    public static void WriteSettings(LidGuardSettings settings)
    {
        var normalizedSettings = LidGuardSettings.Normalize(settings);
        var powerRequest = normalizedSettings.PowerRequest ?? PowerRequestOptions.Default;
        Console.WriteLine(LidGuardText.SettingsTitle);
        Console.WriteLine(LidGuardText.SettingsPreventSystemSleep(LidGuardText.DisplayBoolean(powerRequest.PreventSystemSleep)));
#if !LIDGUARD_LINUX && !LIDGUARD_MACOS
        Console.WriteLine(LidGuardText.SettingsPreventAwayModeSleep(LidGuardText.DisplayBoolean(powerRequest.PreventAwayModeSleep)));
#endif
        Console.WriteLine(LidGuardText.SettingsPreventDisplaySleep(LidGuardText.DisplayBoolean(powerRequest.PreventDisplaySleep)));
        Console.WriteLine(LidGuardText.SettingsChangeLidAction(LidGuardText.DisplayBoolean(normalizedSettings.ChangeLidAction)));
        Console.WriteLine(LidGuardText.SettingsWatchParentProcess(LidGuardText.DisplayBoolean(normalizedSettings.WatchParentProcess)));
        Console.WriteLine(LidGuardText.SettingsSessionTimeout(LidGuardText.DisplayMinuteCount(normalizedSettings.SessionTimeoutMinutes)));
        Console.WriteLine(LidGuardText.SettingsServerRuntimeCleanupDelay(LidGuardText.DisplayMinuteCount(normalizedSettings.ServerRuntimeCleanupDelayMinutes)));
        Console.WriteLine(LidGuardText.SettingsEmergencyHibernationOnHighTemperature(LidGuardText.DisplayBoolean(normalizedSettings.EmergencyHibernationOnHighTemperature)));
        Console.WriteLine(LidGuardText.SettingsEmergencyHibernationTemperatureMode(LidGuardText.DisplayEmergencyHibernationTemperatureMode(normalizedSettings.EmergencyHibernationTemperatureMode)));
        Console.WriteLine(LidGuardText.SettingsEmergencyHibernationTemperatureCelsius(normalizedSettings.EmergencyHibernationTemperatureCelsius));
        Console.WriteLine(LidGuardText.SettingsSuspendMode(LidGuardText.DisplaySuspendMode(normalizedSettings.SuspendMode)));
        Console.WriteLine(LidGuardText.SettingsPostStopSuspendDelaySeconds(normalizedSettings.PostStopSuspendDelaySeconds));
        Console.WriteLine(LidGuardText.SettingsPostStopSuspendSound(LidGuardText.DisplayOptionalValue(PostStopSuspendSoundConfiguration.GetDisplayValue(normalizedSettings.PostStopSuspendSound))));
        Console.WriteLine(LidGuardText.SettingsPostStopSuspendSoundVolumeOverridePercent(LidGuardText.DisplayOptionalValue(PostStopSuspendSoundConfiguration.GetVolumeOverrideDisplayValue(normalizedSettings.PostStopSuspendSoundVolumeOverridePercent))));
        Console.WriteLine(LidGuardText.SettingsSuspendHistoryCount(LidGuardText.DisplaySuspendHistoryEntryCount(normalizedSettings.SuspendHistoryEntryCount)));
        Console.WriteLine(LidGuardText.SettingsPreSuspendWebhookUrl(LidGuardText.DisplayOptionalValue(PreSuspendWebhookConfiguration.GetDisplayValue(normalizedSettings.PreSuspendWebhookUrl))));
        Console.WriteLine(LidGuardText.SettingsPostSessionEndWebhookUrl(LidGuardText.DisplayOptionalValue(PostSessionEndWebhookConfiguration.GetDisplayValue(normalizedSettings.PostSessionEndWebhookUrl))));
        Console.WriteLine(LidGuardText.SettingsClosedLidPermissionRequestDecision(LidGuardText.DisplayClosedLidPermissionRequestDecision(normalizedSettings.ClosedLidPermissionRequestDecision)));
        Console.WriteLine(LidGuardText.SettingsUserInterfaceCulture(UserInterfaceCultureConfiguration.GetDisplayValue(normalizedSettings.UserInterfaceCulture)));
        Console.WriteLine(LidGuardText.SettingsReason(powerRequest.Reason));
    }

    public static int WriteHelp(int exitCode)
    {
        var helpDocument = CreateHelpDocument();
        foreach (var helpSection in LidGuardHelpContent.CreateSummarySections(helpDocument)) WriteHelpSection(helpSection);
        Console.WriteLine(LidGuardText.HelpCommandSpecificHelpHint);
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
        Console.Error.WriteLine(LidGuardText.CommandUnknownCommand(commandName));
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
        foreach (var helpOption in helpCommand.Options) Console.WriteLine($"    {LidGuardText.HelpOptionLabel(helpOption.Label, helpOption.Description)}");
        foreach (var note in helpCommand.Notes) Console.WriteLine($"    {LidGuardText.HelpNoteLabel}: {note}");
    }

    private static string DescribeSoftLockStatus(LidGuardSessionStatus session)
    {
        if (session.SoftLockState != LidGuardSessionSoftLockState.SoftLocked) return LidGuardText.DisplaySessionSoftLockState(session.SoftLockState);

        var details = LidGuardText.DisplaySessionSoftLockState(session.SoftLockState);
        if (!string.IsNullOrWhiteSpace(session.SoftLockReason)) details = $"{details}:{session.SoftLockReason}";
        if (session.SoftLockedAt is not null) details = $"{details}@{LidGuardCommandTimestampFormatter.FormatDisplayTimestamp(session.SoftLockedAt.Value)}";
        return details;
    }
}
