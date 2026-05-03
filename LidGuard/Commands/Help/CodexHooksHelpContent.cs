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
            $"{commandDisplayName} codex-hooks [config-toml|toml|hooks-json|json]",
            "Print a managed Codex hook configuration snippet.",
            [
                new LidGuardHelpOption("<format>", "Optional positional value. Defaults to config-toml. Accepts config-toml, toml, hooks-json, or json.")
            ],
            []);
    }
}
