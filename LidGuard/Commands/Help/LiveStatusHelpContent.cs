using LidGuard.Ipc;
using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class LiveStatusHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(LidGuardPipeCommands.LiveStatus, [], LidGuardHelpSectionTitles.SessionControl, $"{commandDisplayName} {LidGuardPipeCommands.LiveStatus}", LocalizationService.GetString("Help_LiveStatus_Description"), [], [LocalizationService.GetString("Help_LiveStatus_RuntimeNotStartedNote"), LocalizationService.GetString("Help_LiveStatus_ExitNote")]);
    }
}
