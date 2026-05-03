using LidGuard.Ipc;

namespace LidGuard.Commands.Help;

internal static class CopilotHooksHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            LidGuardPipeCommands.CopilotHooks,
            [],
            LidGuardHelpSectionTitles.HookIntegration,
            $"{commandDisplayName} copilot-hooks [config-json|json|hooks-json]",
            "Print a managed GitHub Copilot CLI hook configuration snippet.",
            [
                new LidGuardHelpOption("<format>", "Optional positional value. Defaults to config-json. Accepts config-json, json, or hooks-json.")
            ],
            []);
    }
}
