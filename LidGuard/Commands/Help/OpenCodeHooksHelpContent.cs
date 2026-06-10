using LidGuard.Ipc;
using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class OpenCodeHooksHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(LidGuardPipeCommands.OpenCodeHooks, [], LidGuardHelpSectionTitles.HookIntegration, $"{commandDisplayName} opencode-hooks [plugin-js|js|javascript]", LocalizationService.GetString("Help_OpenCodeHooks_Description"), [new LidGuardHelpOption("<format>", LocalizationService.GetString("Help_OpenCodeHooks_FormatOption"))], []);
    }
}
