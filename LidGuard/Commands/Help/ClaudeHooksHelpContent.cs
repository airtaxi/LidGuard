using LidGuard.Ipc;
using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class ClaudeHooksHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(LidGuardPipeCommands.ClaudeHooks, [], LidGuardHelpSectionTitles.HookIntegration, $"{commandDisplayName} claude-hooks [settings-json|json|hooks-json]", LocalizationService.GetString("Help_ClaudeHooks_Description"), [new LidGuardHelpOption("<format>", LocalizationService.GetString("Help_ClaudeHooks_FormatOption"))], []);
    }
}
