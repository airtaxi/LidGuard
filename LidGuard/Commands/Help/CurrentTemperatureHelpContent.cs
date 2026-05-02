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
            $"{commandDisplayName} {LidGuardPipeCommands.CurrentTemperature} [--temperature-mode default|low|average|high]",
            "Report the current recognized system thermal-zone temperature in Celsius using the selected aggregation mode.",
            [
                new LidGuardHelpOption("--temperature-mode default|low|average|high", "Optional. Use the saved LidGuard setting with default, or override it with low, average, or high for this command only.")
            ],
            [
                "If Windows does not currently expose thermal-zone temperature data, the command reports that the value is unavailable.",
                "When the settings file does not exist yet, default uses LidGuard's headless runtime default mode: Average."
            ]);
    }
}
