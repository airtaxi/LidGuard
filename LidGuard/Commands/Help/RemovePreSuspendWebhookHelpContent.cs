using LidGuard.Ipc;
using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class RemovePreSuspendWebhookHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(LidGuardPipeCommands.RemovePreSuspendWebhook, [], LidGuardHelpSectionTitles.SettingsAndSuspend, $"{commandDisplayName} remove-pre-suspend-webhook", LocalizationService.GetString("Help_RemovePreSuspendWebhook_Description"), [], [LocalizationService.GetString("Help_Command_NoOptionsNote")]);
    }
}
