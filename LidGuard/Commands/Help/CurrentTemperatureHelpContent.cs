using LidGuard.Ipc;

namespace LidGuard.Commands.Help;

internal static class CurrentTemperatureHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            LidGuardPipeCommands.CurrentTemperature,
            [],
            LidGuardHelpSectionTitles.Diagnostics,
            $"{commandDisplayName} {LidGuardPipeCommands.CurrentTemperature} [default|low|average|high]",
            "Report the current system temperature in Celsius using the selected temperature mode.",
            [
                new LidGuardHelpOption("<mode>", "Optional positional value. Use default to follow the saved setting, or choose low, average, or high for this command only.")
            ],
            [
                "If no supported temperature sensor data is available on this platform, the command reports that the value is unavailable.",
                "When the settings file does not exist yet, default uses Average."
            ]);
    }
}
