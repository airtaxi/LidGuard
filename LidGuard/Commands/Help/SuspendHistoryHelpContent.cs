using LidGuard.Ipc;
using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class SuspendHistoryHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(LidGuardPipeCommands.SuspendHistory, [], LidGuardHelpSectionTitles.Diagnostics, $"{commandDisplayName} {LidGuardPipeCommands.SuspendHistory} [count]", LocalizationService.GetString("Help_SuspendHistory_Description"), [new LidGuardHelpOption("<count>", LocalizationService.GetString("Help_SuspendHistory_CountOption"))], [LocalizationService.GetString("Help_SuspendHistory_CountNote")]);
    }
}
