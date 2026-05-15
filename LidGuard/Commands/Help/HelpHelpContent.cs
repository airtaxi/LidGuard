using LidGuard.Ipc;
using LidGuard.Localization;

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
            LocalizationService.GetString("Help_Help_Description"),
            [],
            [
                LocalizationService.GetString("Help_Help_CommandOptionNote")
            ]);
    }
}
