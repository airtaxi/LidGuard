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
            $"{commandDisplayName} copilot-hooks [--format config-json|hooks-json]",
            "Print a managed GitHub Copilot CLI hook configuration snippet.",
            [
                new LidGuardHelpOption("--format <format>", "Optional. Defaults to config-json. Also accepts json or hooks-json.")
            ],
            []);
    }
}
