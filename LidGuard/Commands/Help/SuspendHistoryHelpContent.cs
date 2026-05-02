using LidGuard.Ipc;

namespace LidGuard.Commands.Help;

internal static class SuspendHistoryHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            LidGuardPipeCommands.SuspendHistory,
            [],
            LidGuardHelpSectionTitles.Diagnostics,
            $"{commandDisplayName} {LidGuardPipeCommands.SuspendHistory} [--count <number>]",
            "Print recent suspend request history from the local suspend history log.",
            [
                new LidGuardHelpOption("--count <number>", "Optional positive entry count to display. Defaults to the saved suspend-history-count value, or 10 when recording is off.")
            ],
            [
                "The saved suspend-history-count setting controls how many entries are retained. The --count option only limits how many retained entries are displayed."
            ]);
    }
}
