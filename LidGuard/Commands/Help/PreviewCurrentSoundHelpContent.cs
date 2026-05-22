using LidGuard.Ipc;
using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class PreviewCurrentSoundHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(LidGuardPipeCommands.PreviewCurrentSound, [], LidGuardHelpSectionTitles.SettingsAndSuspend, $"{commandDisplayName} preview-current-sound", LocalizationService.GetString("Help_PreviewCurrentSound_Description"), [], [LocalizationService.GetString("Help_PreviewCurrentSound_NoSoundNote"), LocalizationService.GetString("Help_SoundPreview_WaitsForPlaybackNote")]);
    }
}
