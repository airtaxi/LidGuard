using LidGuard.Ipc;

namespace LidGuard.Commands.Help;

internal static class LiveStatusHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            LidGuardPipeCommands.LiveStatus,
            [],
            LidGuardHelpSectionTitles.SessionControl,
            $"{commandDisplayName} {LidGuardPipeCommands.LiveStatus}",
            "Show a live terminal dashboard for runtime state, active sessions, and recent LidGuard flow logs.",
            [],
            [
                "This command does not start the runtime; it waits and reconnects when the runtime is unavailable.",
                "Press q, Escape, or Ctrl+C to exit."
            ]);
    }
}
