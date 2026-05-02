using LidGuard.Ipc;

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
            "Report the current visible display monitor count using the same base platform monitor visibility check LidGuard uses for closed-lid policy decisions.",
            [],
            [
                "Internal laptop panel connections are only excluded by the final suspend eligibility check."
            ]);
    }
}
