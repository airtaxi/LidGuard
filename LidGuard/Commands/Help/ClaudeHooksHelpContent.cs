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
            $"{commandDisplayName} claude-hooks [--format settings-json|hooks-json]",
            "Print a managed Claude Code hook configuration snippet.",
            [
                new LidGuardHelpOption("--format <format>", "Optional. Defaults to settings-json. Also accepts json or hooks-json.")
            ],
            []);
    }
}
