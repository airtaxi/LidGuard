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
            new LidGuardHelpSectionEntry(
                LidGuardHelpSectionTitles.ManagedAndInternalCommands,
                [
                    LidGuardHelpTextLocalizer.Localize("These commands are intended for provider-managed integrations and stdio hosts rather than direct everyday CLI use.")
                ]),
            new LidGuardHelpSectionEntry(
                LidGuardHelpSectionTitles.PathsAndNotes,
                CreatePathsAndNotesDetails(
                    documentContext.SettingsFilePath,
                    documentContext.SessionLogFilePath,
                    documentContext.SuspendHistoryLogFilePath))
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
            "Use --name value or --name=value for options.",
            "Boolean options accept true/false, yes/no, on/off, and 1/0.",
            "Quote paths or text values when they contain spaces."
        ];
    }

    private static IReadOnlyList<string> CreatePathsAndNotesDetails(
        string settingsFilePath,
        string sessionLogFilePath,
        string suspendHistoryLogFilePath)
    {
        return
        [
            LidGuardText.ConsoleSettingsFile(settingsFilePath),
            LidGuardText.HelpSessionLogFile(sessionLogFilePath),
            LidGuardText.HelpSuspendHistoryLogFile(suspendHistoryLogFilePath),
#if LIDGUARD_LINUX
            "Linux support is implemented for systemd/logind systems. macOS support is implemented in macOS builds.",
#elif LIDGUARD_MACOS
            "macOS support uses caffeinate and pmset. Windows and Linux support is implemented in their platform builds.",
#else
            "This build includes Windows support. Linux and macOS support is implemented in their platform builds.",
#endif
            "Provider MCP integrations are best-effort only because correct behavior depends on the model calling the LidGuard MCP tools at the right times."
        ];
    }
}
