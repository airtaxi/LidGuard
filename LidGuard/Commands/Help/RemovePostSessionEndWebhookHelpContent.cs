using LidGuard.Ipc;

namespace LidGuard.Commands.Help;

internal static class RemovePostSessionEndWebhookHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            LidGuardPipeCommands.RemovePostSessionEndWebhook,
            [],
            LidGuardHelpSectionTitles.SettingsAndSuspend,
            $"{commandDisplayName} remove-post-session-end-webhook",
            "Clear the saved session completion webhook URL.",
            [],
            [
                "This command does not accept any options."
            ]);
    }
}
