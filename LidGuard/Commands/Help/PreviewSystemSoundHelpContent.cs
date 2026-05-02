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
            $"{commandDisplayName} preview-system-sound --name Asterisk|Beep|Exclamation|Hand|Question",
            "Play one supported SystemSound name immediately using the saved post-stop suspend sound volume override setting.",
            [
                new LidGuardHelpOption("--name <sound>", "Required. Allowed values: Asterisk, Beep, Exclamation, Hand, or Question.")
            ],
            [
                "This command waits until playback finishes."
            ]);
    }
}
