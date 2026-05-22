using LidGuard.Ipc;
using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class CleanupOrphansHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(LidGuardPipeCommands.CleanupOrphans, [], LidGuardHelpSectionTitles.SessionControl, $"{commandDisplayName} cleanup-orphans", LocalizationService.GetString("Help_CleanupOrphans_Description"), [], [LocalizationService.GetString("Help_CleanupOrphans_RuntimeNotRunningNote")]);
    }
}
