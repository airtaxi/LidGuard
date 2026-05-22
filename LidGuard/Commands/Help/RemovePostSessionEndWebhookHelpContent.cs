using LidGuard.Ipc;
using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class RemovePostSessionEndWebhookHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(LidGuardPipeCommands.RemovePostSessionEndWebhook, [], LidGuardHelpSectionTitles.SettingsAndSuspend, $"{commandDisplayName} remove-post-session-end-webhook", LocalizationService.GetString("Help_RemovePostSessionEndWebhook_Description"), [], [LocalizationService.GetString("Help_Command_NoOptionsNote")]);
    }
}
