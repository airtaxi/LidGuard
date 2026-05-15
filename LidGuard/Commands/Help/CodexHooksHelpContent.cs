using LidGuard.Ipc;
using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class CodexHooksHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            LidGuardPipeCommands.CodexHooks,
            [],
            LidGuardHelpSectionTitles.HookIntegration,
            $"{commandDisplayName} codex-hooks [config-toml|toml|hooks-json|json]",
            LocalizationService.GetString("Help_CodexHooks_Description"),
            [
                new LidGuardHelpOption("<format>", LocalizationService.GetString("Help_CodexHooks_FormatOption"))
            ],
            []);
    }
}
