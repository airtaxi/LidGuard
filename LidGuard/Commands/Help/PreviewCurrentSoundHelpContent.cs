using LidGuard.Ipc;

namespace LidGuard.Commands.Help;

internal static class PreviewCurrentSoundHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            LidGuardPipeCommands.PreviewCurrentSound,
            [],
            LidGuardHelpSectionTitles.SettingsAndSuspend,
            $"{commandDisplayName} preview-current-sound",
            "Play the saved post-stop suspend sound immediately using the saved volume override setting.",
            [],
            [
                "If no post-stop suspend sound is configured, this command prints settings guidance instead of failing.",
                "This command waits until playback finishes."
            ]);
    }
}
