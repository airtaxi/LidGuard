using LidGuard.Ipc;

namespace LidGuard.Commands.Help;

internal static class StatusHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            LidGuardPipeCommands.Status,
            [],
            LidGuardHelpSectionTitles.SessionControl,
            $"{commandDisplayName} status",
            "Show runtime state, active sessions, and effective stored settings.",
            [],
            [
                "If the runtime is not running, status still prints the stored settings file contents."
            ]);
    }
}
