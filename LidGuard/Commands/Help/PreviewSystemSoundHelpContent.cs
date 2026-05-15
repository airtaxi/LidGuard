using LidGuard.Ipc;
using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class PreviewSystemSoundHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            LidGuardPipeCommands.PreviewSystemSound,
            [],
            LidGuardHelpSectionTitles.SettingsAndSuspend,
            $"{commandDisplayName} preview-system-sound Asterisk|Beep|Exclamation|Hand|Question",
            LocalizationService.GetString("Help_PreviewSystemSound_Description"),
            [
                new LidGuardHelpOption("<sound>", LocalizationService.GetString("Help_PreviewSystemSound_NameOption"))
            ],
            [
                LocalizationService.GetString("Help_SoundPreview_WaitsForPlaybackNote")
            ]);
    }
}
