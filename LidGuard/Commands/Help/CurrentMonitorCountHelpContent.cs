using LidGuard.Ipc;
using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class CurrentMonitorCountHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            LidGuardPipeCommands.CurrentMonitorCount,
            [],
            LidGuardHelpSectionTitles.Diagnostics,
            $"{commandDisplayName} {LidGuardPipeCommands.CurrentMonitorCount}",
            LocalizationService.GetString("Help_CurrentMonitorCount_Description"),
            [],
            [
                LocalizationService.GetString("Help_CurrentMonitorCount_InternalPanelNote")
            ]);
    }
}
