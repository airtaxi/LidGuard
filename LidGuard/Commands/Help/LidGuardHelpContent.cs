namespace LidGuard.Commands.Help;

internal static class LidGuardHelpContent
{
    internal static LidGuardHelpDocument CreateDocument(
        string commandDisplayName,
        string settingsFilePath,
        string sessionLogFilePath,
        string suspendHistoryLogFilePath,
        string supportedPostStopSuspendSystemSounds)
    {
        var documentContext = new LidGuardHelpDocumentContext(
            commandDisplayName,
            settingsFilePath,
            sessionLogFilePath,
            suspendHistoryLogFilePath,
            supportedPostStopSuspendSystemSounds);

        return new LidGuardHelpDocument(
            documentContext,
            LidGuardHelpSectionCatalog.CreateSectionEntries(documentContext),
            LidGuardHelpCommandCatalog.CreateCommandEntries(documentContext));
    }

    internal static IReadOnlyList<LidGuardHelpSection> CreateSections(
        string commandDisplayName,
        string settingsFilePath,
        string sessionLogFilePath,
        string suspendHistoryLogFilePath,
        string supportedPostStopSuspendSystemSounds)
        => CreateAllSections(CreateDocument(
            commandDisplayName,
            settingsFilePath,
            sessionLogFilePath,
            suspendHistoryLogFilePath,
            supportedPostStopSuspendSystemSounds));

    internal static bool TryFindCommand(
        LidGuardHelpDocument document,
        string commandName,
        out LidGuardHelpCommandEntry commandEntry)
    {
        commandEntry = default;
        var normalizedCommandName = commandName.Trim();
        if (string.IsNullOrWhiteSpace(normalizedCommandName)) return false;

        foreach (var candidateCommandEntry in document.CommandEntries)
        {
            if (candidateCommandEntry.CanonicalName.Equals(normalizedCommandName, StringComparison.OrdinalIgnoreCase))
            {
                commandEntry = candidateCommandEntry;
                return true;
            }

            foreach (var alias in candidateCommandEntry.Aliases)
            {
                if (!alias.Equals(normalizedCommandName, StringComparison.OrdinalIgnoreCase)) continue;

                commandEntry = candidateCommandEntry;
                return true;
            }
        }

        return false;
    }

    internal static IReadOnlyList<LidGuardHelpSection> CreateAllSections(LidGuardHelpDocument document)
    {
        var helpSections = new List<LidGuardHelpSection>();
        foreach (var sectionEntry in document.SectionEntries)
        {
            var helpCommands = CreateHelpCommandsForSection(document, sectionEntry.Title);
            helpSections.Add(new LidGuardHelpSection(sectionEntry.Title, sectionEntry.Details, helpCommands));
        }

        return helpSections;
    }

    internal static IReadOnlyList<LidGuardHelpSection> CreateSummarySections(LidGuardHelpDocument document)
    {
        var helpSections = new List<LidGuardHelpSection>
        {
            new(
                LidGuardHelpSectionTitles.Usage,
                LidGuardHelpSectionCatalog.CreateSummaryUsageDetails(document.Context.CommandDisplayName),
                [])
        };

        foreach (var sectionEntry in document.SectionEntries)
        {
            if (sectionEntry.Title.Equals(LidGuardHelpSectionTitles.Usage, StringComparison.Ordinal)) continue;
            if (sectionEntry.Title.Equals(LidGuardHelpSectionTitles.PathsAndNotes, StringComparison.Ordinal)) continue;

            var helpCommands = CreateSummaryCommandsForSection(document, sectionEntry.Title);
            if (helpCommands.Count == 0) continue;

            helpSections.Add(new LidGuardHelpSection(sectionEntry.Title, sectionEntry.Details, helpCommands));
        }

        return helpSections;
    }

    internal static IReadOnlyList<LidGuardHelpSection> CreateCommandSections(
        LidGuardHelpDocument document,
        LidGuardHelpCommandEntry commandEntry)
    {
        return
        [
            new LidGuardHelpSection(LidGuardHelpSectionTitles.Usage, CreateCommandUsageDetails(commandEntry), []),
            new LidGuardHelpSection(
                commandEntry.SectionTitle,
                CreateSectionDetails(document, commandEntry.SectionTitle),
                commandEntry.HelpCommands)
        ];
    }

    private static IReadOnlyList<string> CreateCommandUsageDetails(LidGuardHelpCommandEntry commandEntry)
    {
        var usageDetails = new List<string>();
        foreach (var helpCommand in commandEntry.HelpCommands) usageDetails.Add(helpCommand.Synopsis);
        return usageDetails;
    }

    private static IReadOnlyList<string> CreateSectionDetails(LidGuardHelpDocument document, string sectionTitle)
    {
        foreach (var sectionEntry in document.SectionEntries)
        {
            if (sectionEntry.Title.Equals(sectionTitle, StringComparison.Ordinal)) return sectionEntry.Details;
        }

        return [];
    }

    private static IReadOnlyList<LidGuardHelpCommand> CreateHelpCommandsForSection(LidGuardHelpDocument document, string sectionTitle)
    {
        var helpCommands = new List<LidGuardHelpCommand>();
        foreach (var commandEntry in document.CommandEntries)
        {
            if (!commandEntry.SectionTitle.Equals(sectionTitle, StringComparison.Ordinal)) continue;
            foreach (var helpCommand in commandEntry.HelpCommands) helpCommands.Add(helpCommand);
        }

        return helpCommands;
    }

    private static IReadOnlyList<LidGuardHelpCommand> CreateSummaryCommandsForSection(LidGuardHelpDocument document, string sectionTitle)
    {
        var helpCommands = new List<LidGuardHelpCommand>();
        foreach (var commandEntry in document.CommandEntries)
        {
            if (!commandEntry.SectionTitle.Equals(sectionTitle, StringComparison.Ordinal)) continue;

            helpCommands.Add(new LidGuardHelpCommand(
                CreateSummaryCommandLabel(commandEntry),
                commandEntry.SummaryDescription,
                [],
                []));
        }

        return helpCommands;
    }

    private static string CreateSummaryCommandLabel(LidGuardHelpCommandEntry commandEntry)
    {
        if (commandEntry.Aliases.Count == 0) return commandEntry.CanonicalName;

        return $"{commandEntry.CanonicalName} (alias: {string.Join(", ", commandEntry.Aliases)})";
    }
}
