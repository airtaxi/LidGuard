using LidGuard.Ipc;
using LidGuard.Localization;

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
            LocalizationService.GetString("Help_CurrentTemperature_Description"),
            [
                new LidGuardHelpOption("<mode>", LocalizationService.GetString("Help_CurrentTemperature_TemperatureModeOption"))
            ],
            [
                LocalizationService.GetString("Help_CurrentTemperature_UnavailableNote"),
                LocalizationService.GetString("Help_CurrentTemperature_DefaultModeNote")
            ]);
    }
}
