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
        Console.WriteLine(LocalizationService.GetFormattedString("ConsoleActiveSessions", response.ActiveSessionCount));
        Console.WriteLine(LocalizationService.GetFormattedString("ConsoleLidState", LocalizationService.DisplayLidSwitchState(response.LidSwitchState)));
        Console.WriteLine(LocalizationService.GetFormattedString("ConsoleVisibleDisplayMonitorCount", response.VisibleDisplayMonitorCount));

        if (includeSessions)
        {
            foreach (var session in response.Sessions)
            {
                var processText = session.WatchedProcessIdentifier > 0 ? session.WatchedProcessIdentifier.ToString() : LocalizationService.GetString("SessionProcessNone");
                var providerDisplayText = AgentProviderDisplay.CreateProviderDisplayText(session.Provider, session.ProviderName);
                var startedAt = LidGuardCommandTimestampFormatter.FormatDisplayTimestamp(session.StartedAt);
                var lastActivityAt = LidGuardCommandTimestampFormatter.FormatDisplayTimestamp(session.LastActivityAt);
                Console.WriteLine(
                    LocalizationService.GetFormattedString("ConsoleSessionLine",
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
        Console.WriteLine(LocalizationService.GetString("SettingsTitle"));
        Console.WriteLine(LocalizationService.GetFormattedString("SettingsPreventSystemSleep", LocalizationService.DisplayBoolean(powerRequest.PreventSystemSleep)));
#if !LIDGUARD_LINUX && !LIDGUARD_MACOS
        Console.WriteLine(LocalizationService.GetFormattedString("SettingsPreventAwayModeSleep", LocalizationService.DisplayBoolean(powerRequest.PreventAwayModeSleep)));
#endif
        Console.WriteLine(LocalizationService.GetFormattedString("SettingsPreventDisplaySleep", LocalizationService.DisplayBoolean(powerRequest.PreventDisplaySleep)));
        Console.WriteLine(LocalizationService.GetFormattedString("SettingsChangeLidAction", LocalizationService.DisplayBoolean(normalizedSettings.ChangeLidAction)));
        Console.WriteLine(LocalizationService.GetFormattedString("SettingsWatchParentProcess", LocalizationService.DisplayBoolean(normalizedSettings.WatchParentProcess)));
        Console.WriteLine(LocalizationService.GetFormattedString("SettingsSessionTimeout", LocalizationService.DisplayMinuteCount(normalizedSettings.SessionTimeoutMinutes)));
        Console.WriteLine(LocalizationService.GetFormattedString("SettingsServerRuntimeCleanupDelay", LocalizationService.DisplayMinuteCount(normalizedSettings.ServerRuntimeCleanupDelayMinutes)));
        Console.WriteLine(LocalizationService.GetFormattedString("SettingsEmergencyHibernationOnHighTemperature", LocalizationService.DisplayBoolean(normalizedSettings.EmergencyHibernationOnHighTemperature)));
        Console.WriteLine(LocalizationService.GetFormattedString("SettingsEmergencyHibernationTemperatureMode", LocalizationService.DisplayEmergencyHibernationTemperatureMode(normalizedSettings.EmergencyHibernationTemperatureMode)));
        Console.WriteLine(LocalizationService.GetFormattedString("SettingsEmergencyHibernationTemperatureCelsius", normalizedSettings.EmergencyHibernationTemperatureCelsius));
        Console.WriteLine(LocalizationService.GetFormattedString("SettingsSuspendMode", LocalizationService.DisplaySuspendMode(normalizedSettings.SuspendMode)));
        Console.WriteLine(LocalizationService.GetFormattedString("SettingsPostStopSuspendDelaySeconds", normalizedSettings.PostStopSuspendDelaySeconds));
        Console.WriteLine(LocalizationService.GetFormattedString("SettingsPostStopSuspendSound", LocalizationService.DisplayOptionalValue(PostStopSuspendSoundConfiguration.GetDisplayValue(normalizedSettings.PostStopSuspendSound))));
        Console.WriteLine(LocalizationService.GetFormattedString("SettingsPostStopSuspendSoundVolumeOverridePercent", LocalizationService.DisplayOptionalValue(PostStopSuspendSoundConfiguration.GetVolumeOverrideDisplayValue(normalizedSettings.PostStopSuspendSoundVolumeOverridePercent))));
        Console.WriteLine(LocalizationService.GetFormattedString("SettingsSuspendHistoryCount", LocalizationService.DisplaySuspendHistoryEntryCount(normalizedSettings.SuspendHistoryEntryCount)));
        Console.WriteLine(LocalizationService.GetFormattedString("SettingsPreSuspendWebhookUrl", LocalizationService.DisplayOptionalValue(PreSuspendWebhookConfiguration.GetDisplayValue(normalizedSettings.PreSuspendWebhookUrl))));
        Console.WriteLine(LocalizationService.GetFormattedString("SettingsPostSessionEndWebhookUrl", LocalizationService.DisplayOptionalValue(PostSessionEndWebhookConfiguration.GetDisplayValue(normalizedSettings.PostSessionEndWebhookUrl))));
        Console.WriteLine(LocalizationService.GetFormattedString("SettingsClosedLidPermissionRequestDecision", LocalizationService.DisplayClosedLidPermissionRequestDecision(normalizedSettings.ClosedLidPermissionRequestDecision)));
        Console.WriteLine(LocalizationService.GetFormattedString("SettingsUserInterfaceCulture", UserInterfaceCultureConfiguration.GetDisplayValue(normalizedSettings.UserInterfaceCulture)));
        Console.WriteLine(LocalizationService.GetFormattedString("SettingsReason", powerRequest.Reason));
    }

    public static int WriteHelp(int exitCode)
    {
        var helpDocument = CreateHelpDocument();
        foreach (var helpSection in LidGuardHelpContent.CreateSummarySections(helpDocument)) WriteHelpSection(helpSection);
        Console.WriteLine(LocalizationService.GetString("HelpCommandSpecificHelpHint"));
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
        Console.Error.WriteLine(LocalizationService.GetFormattedString("CommandUnknownCommand", commandName));
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
        foreach (var helpOption in helpCommand.Options) Console.WriteLine($"    {LocalizationService.GetFormattedString("HelpOptionLabel", helpOption.Label, helpOption.Description)}");
        foreach (var note in helpCommand.Notes) Console.WriteLine($"    {LocalizationService.GetString("HelpNoteLabel")}: {note}");
    }

    private static string DescribeSoftLockStatus(LidGuardSessionStatus session)
    {
        if (session.SoftLockState != LidGuardSessionSoftLockState.SoftLocked) return LocalizationService.DisplaySessionSoftLockState(session.SoftLockState);

        var details = LocalizationService.DisplaySessionSoftLockState(session.SoftLockState);
        if (!string.IsNullOrWhiteSpace(session.SoftLockReason)) details = $"{details}:{session.SoftLockReason}";
        if (session.SoftLockedAt is not null) details = $"{details}@{LidGuardCommandTimestampFormatter.FormatDisplayTimestamp(session.SoftLockedAt.Value)}";
        return details;
    }
}
