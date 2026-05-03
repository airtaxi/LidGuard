using LidGuard.Ipc;

namespace LidGuard.Commands.Help;

internal static class RemovePreSuspendWebhookHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            LidGuardPipeCommands.RemovePreSuspendWebhook,
            [],
            LidGuardHelpSectionTitles.SettingsAndSuspend,
            $"{commandDisplayName} remove-pre-suspend-webhook",
            "Clear the saved before sleep or hibernate webhook URL.",
            [],
            [
                "This command does not accept any options."
            ]);
    }
}
