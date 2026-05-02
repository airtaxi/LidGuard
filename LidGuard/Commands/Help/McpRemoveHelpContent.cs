using LidGuard.Ipc;

namespace LidGuard.Commands.Help;

internal static class McpRemoveHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            LidGuardPipeCommands.McpRemove,
            ["mcp-uninstall"],
            LidGuardHelpSectionTitles.McpIntegration,
            $"{commandDisplayName} mcp-remove [--provider codex|claude|copilot|all]",
            "Remove the managed stdio MCP server named lidguard from the selected provider CLI.",
            [
                new LidGuardHelpOption("--provider <provider>", "Optional. Allowed values: codex, claude, copilot, or all. When omitted, LidGuard prompts for a provider.")
            ],
            [
                "With --provider all, only providers whose default configuration roots already exist are processed. Missing providers are reported and skipped."
            ]);
    }
}
