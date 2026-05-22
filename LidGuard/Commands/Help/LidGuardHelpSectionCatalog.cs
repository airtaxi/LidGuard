using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class LidGuardHelpSectionCatalog
{
    internal static IReadOnlyList<LidGuardHelpSectionEntry> CreateSectionEntries(LidGuardHelpDocumentContext documentContext)
    {
        return
        [
            new LidGuardHelpSectionEntry(LidGuardHelpSectionTitles.Usage, CreateUsageDetails(documentContext.CommandDisplayName)),
            new LidGuardHelpSectionEntry(LidGuardHelpSectionTitles.SessionControl, []),
            new LidGuardHelpSectionEntry(LidGuardHelpSectionTitles.SettingsAndSuspend, []),
            new LidGuardHelpSectionEntry(LidGuardHelpSectionTitles.Diagnostics, []),
            new LidGuardHelpSectionEntry(LidGuardHelpSectionTitles.HookIntegration, []),
            new LidGuardHelpSectionEntry(LidGuardHelpSectionTitles.McpIntegration, []),
            new LidGuardHelpSectionEntry(LidGuardHelpSectionTitles.ManagedAndInternalCommands, [LocalizationService.GetString("Help_ManagedCommands_Detail")]),
            new LidGuardHelpSectionEntry(LidGuardHelpSectionTitles.PathsAndNotes, CreatePathsAndNotesDetails(documentContext.SettingsFilePath, documentContext.SessionLogFilePath, documentContext.SuspendHistoryLogFilePath))
        ];
    }

    internal static IReadOnlyList<string> CreateSummaryUsageDetails(string commandDisplayName)
    {
        return
        [
            $"{commandDisplayName} <command> [options]",
            $"{commandDisplayName} help <command>",
            $"{commandDisplayName} <command> --help"
        ];
    }

    private static IReadOnlyList<string> CreateUsageDetails(string commandDisplayName)
    {
        return
        [
            $"{commandDisplayName} <command> [options]",
            LocalizationService.GetString("Help_Usage_OptionSyntax"),
            LocalizationService.GetString("Help_Usage_BooleanOptions"),
            LocalizationService.GetString("Help_Usage_QuoteValues")
        ];
    }

    private static IReadOnlyList<string> CreatePathsAndNotesDetails(string settingsFilePath, string sessionLogFilePath, string suspendHistoryLogFilePath)
    {
        return
        [
            LocalizationService.GetFormattedString("ConsoleSettingsFile", settingsFilePath),
            LocalizationService.GetFormattedString("HelpSessionLogFile", sessionLogFilePath),
            LocalizationService.GetFormattedString("HelpSuspendHistoryLogFile", suspendHistoryLogFilePath),
#if LIDGUARD_LINUX
            LocalizationService.GetString("Help_Paths_LinuxRuntimeBehavior"),
#elif LIDGUARD_MACOS
            LocalizationService.GetString("Help_Paths_MacOSRuntimeBehavior"),
#else
            LocalizationService.GetString("Help_Paths_WindowsRuntimeBehavior"),
#endif
            LocalizationService.GetString("Help_Paths_ProviderMcpBestEffort")
        ];
    }
}
