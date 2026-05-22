using LidGuard.Ipc;
using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class StatusHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(LidGuardPipeCommands.Status, [], LidGuardHelpSectionTitles.SessionControl, $"{commandDisplayName} status", LocalizationService.GetString("Help_Status_Description"), [], [LocalizationService.GetString("Help_Status_RuntimeNotRunningNote")]);
    }
}
