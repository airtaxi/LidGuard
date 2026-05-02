namespace LidGuard.Commands.Help;

internal static class LidGuardHelpCommandEntryFactory
{
    internal static LidGuardHelpCommandEntry CreateSingleCommandEntry(
        string canonicalName,
        IReadOnlyList<string> aliases,
        string sectionTitle,
        string synopsis,
        string description,
        IReadOnlyList<LidGuardHelpOption> options,
        IReadOnlyList<string> notes)
        => new(
            canonicalName,
            aliases,
            sectionTitle,
            description,
            [
                new LidGuardHelpCommand(synopsis, description, options, notes)
            ]);
}
