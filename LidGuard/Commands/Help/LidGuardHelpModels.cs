namespace LidGuard.Commands.Help;

internal sealed class LidGuardHelpDocument(
    LidGuardHelpDocumentContext context,
    IReadOnlyList<LidGuardHelpSectionEntry> sectionEntries,
    IReadOnlyList<LidGuardHelpCommandEntry> commandEntries)
{
    public LidGuardHelpDocumentContext Context { get; } = context;

    public IReadOnlyList<LidGuardHelpSectionEntry> SectionEntries { get; } = sectionEntries;

    public IReadOnlyList<LidGuardHelpCommandEntry> CommandEntries { get; } = commandEntries;
}

internal readonly record struct LidGuardHelpDocumentContext(
    string CommandDisplayName,
    string SettingsFilePath,
    string SessionLogFilePath,
    string SuspendHistoryLogFilePath,
    string SupportedPostStopSuspendSystemSounds);

internal readonly record struct LidGuardHelpCommandEntry(
    string CanonicalName,
    IReadOnlyList<string> Aliases,
    string SectionTitle,
    string SummaryDescription,
    IReadOnlyList<LidGuardHelpCommand> HelpCommands);

internal readonly record struct LidGuardHelpSectionEntry(
    string Title,
    IReadOnlyList<string> Details);

internal readonly record struct LidGuardHelpSection(
    string Title,
    IReadOnlyList<string> Details,
    IReadOnlyList<LidGuardHelpCommand> Commands);

internal readonly record struct LidGuardHelpCommand(
    string Synopsis,
    string Description,
    IReadOnlyList<LidGuardHelpOption> Options,
    IReadOnlyList<string> Notes);

internal readonly record struct LidGuardHelpOption(string Label, string Description);
