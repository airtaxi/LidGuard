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
                    "These commands are intended for provider-managed integrations and stdio hosts rather than direct everyday CLI use."
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
            $"Settings file: {settingsFilePath}",
            $"Session log: {sessionLogFilePath}",
            $"Suspend history log: {suspendHistoryLogFilePath}",
#if LIDGUARD_LINUX
            "Linux runtime behavior is implemented for systemd/logind systems. macOS currently prints a support-planned message and exits successfully.",
#else
            "This build implements Windows runtime behavior. Linux runtime behavior is implemented in Linux builds; macOS currently prints a support-planned message and exits successfully.",
#endif
            "Provider MCP integrations are best-effort only because correct behavior depends on the model calling the LidGuard MCP tools at the right times."
        ];
    }
}
