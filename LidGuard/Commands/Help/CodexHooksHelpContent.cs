using LidGuard.Ipc;

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
            $"{commandDisplayName} codex-hooks [--format config-toml|hooks-json]",
            "Print a managed Codex hook configuration snippet.",
            [
                new LidGuardHelpOption("--format <format>", "Optional. Defaults to config-toml. Also accepts toml or hooks-json.")
            ],
            []);
    }
}
