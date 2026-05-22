using LidGuard.Ipc;
using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class CopilotHooksHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(LidGuardPipeCommands.CopilotHooks, [], LidGuardHelpSectionTitles.HookIntegration, $"{commandDisplayName} copilot-hooks [config-json|json|hooks-json]", LocalizationService.GetString("Help_CopilotHooks_Description"), [new LidGuardHelpOption("<format>", LocalizationService.GetString("Help_CopilotHooks_FormatOption"))], []);
    }
}
