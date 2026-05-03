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
            "Play the saved sleep or hibernate warning sound now, using the saved temporary volume setting.",
            [],
            [
                "If no warning sound is configured, this command prints settings guidance instead of failing.",
                "This command waits until playback finishes."
            ]);
    }
}
