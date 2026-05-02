using LidGuard.Ipc;

namespace LidGuard.Commands.Help;

internal static class CurrentLidStateHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            LidGuardPipeCommands.CurrentLidState,
            [],
            LidGuardHelpSectionTitles.Diagnostics,
            $"{commandDisplayName} {LidGuardPipeCommands.CurrentLidState}",
            "Report the current lid switch state using the same Windows lid-state source LidGuard uses for closed-lid policy decisions.",
            [],
            [
                "This reports Open, Closed, or Unknown based on the current `GUID_LIDSWITCH_STATE_CHANGE` value."
            ]);
    }
}
