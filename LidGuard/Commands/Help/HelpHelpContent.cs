using LidGuard.Ipc;

namespace LidGuard.Commands.Help;

internal static class HelpHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            LidGuardPipeCommands.Help,
            [],
            LidGuardHelpSectionTitles.SessionControl,
            $"{commandDisplayName} help [command]",
            "Show the categorized command overview or focused detailed help for one known command or alias.",
            [],
            [
                "The <command> --help form uses the same command metadata."
            ]);
    }
}
