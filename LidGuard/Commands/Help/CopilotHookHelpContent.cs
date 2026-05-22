using LidGuard.Ipc;
using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class CopilotHookHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(LidGuardPipeCommands.CopilotHook, [], LidGuardHelpSectionTitles.ManagedAndInternalCommands, $"{commandDisplayName} copilot-hook --event <event-name>", LocalizationService.GetString("Help_CopilotHook_Description"), [new LidGuardHelpOption("--event <event-name>", LocalizationService.GetString("Help_CopilotHook_EventOption"))], []);
    }
}
