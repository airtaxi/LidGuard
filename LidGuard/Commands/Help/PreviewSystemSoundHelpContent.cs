using LidGuard.Ipc;

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
            "Play one supported SystemSound name now, using the saved temporary volume setting.",
            [
                new LidGuardHelpOption("<sound>", "Required positional value. Allowed values: Asterisk, Beep, Exclamation, Hand, or Question.")
            ],
            [
                "This command waits until playback finishes."
            ]);
    }
}
