using LidGuard.Ipc;

namespace LidGuard.Commands.Help;

internal static class ClaudeHooksHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            LidGuardPipeCommands.ClaudeHooks,
            [],
            LidGuardHelpSectionTitles.HookIntegration,
            $"{commandDisplayName} claude-hooks [settings-json|json|hooks-json]",
            "Print a managed Claude Code hook configuration snippet.",
            [
                new LidGuardHelpOption("<format>", "Optional positional value. Defaults to settings-json. Accepts settings-json, json, or hooks-json.")
            ],
            []);
    }
}
