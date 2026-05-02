using LidGuard.Ipc;

namespace LidGuard.Commands.Help;

internal static class CleanupOrphansHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            LidGuardPipeCommands.CleanupOrphans,
            [],
            LidGuardHelpSectionTitles.SessionControl,
            $"{commandDisplayName} cleanup-orphans",
            "Remove sessions whose watched processes have already exited.",
            [],
            [
                "If the runtime is not running, cleanup-orphans reports that nothing needs cleanup."
            ]);
    }
}
