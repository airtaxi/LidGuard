using LidGuard.Ipc;
using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class RemoveClosedLidStopFollowUpWebhookHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext documentContext)
        => LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            LidGuardPipeCommands.RemoveClosedLidStopFollowUpWebhook,
            [],
            LidGuardHelpSectionTitles.SettingsAndSuspend,
            $"{documentContext.CommandDisplayName} {LidGuardPipeCommands.RemoveClosedLidStopFollowUpWebhook}",
            LocalizationService.GetString(
                "Help_RemoveClosedLidStopFollowUpWebhook_Description"),
            [],
            []);
}
