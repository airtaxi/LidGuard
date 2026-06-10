using LidGuard.Ipc;
using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class OpenCodeHookHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(LidGuardPipeCommands.OpenCodeHook, [], LidGuardHelpSectionTitles.ManagedAndInternalCommands, $"{commandDisplayName} opencode-hook --event <event-name>", LocalizationService.GetString("Help_OpenCodeHook_Description"), [new LidGuardHelpOption("--event <event-name>", LocalizationService.GetString("Help_OpenCodeHook_EventOption"))], []);
    }
}
