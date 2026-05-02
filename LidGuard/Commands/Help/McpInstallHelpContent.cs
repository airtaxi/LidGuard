using LidGuard.Ipc;

namespace LidGuard.Commands.Help;

internal static class McpInstallHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            LidGuardPipeCommands.McpInstall,
            [],
            LidGuardHelpSectionTitles.McpIntegration,
            $"{commandDisplayName} mcp-install [--provider codex|claude|copilot|all]",
            "Register or refresh the managed stdio MCP server named lidguard with the selected provider CLI.",
            [
                new LidGuardHelpOption("--provider <provider>", "Optional. Allowed values: codex, claude, copilot, or all. When omitted, LidGuard prompts for a provider.")
            ],
            [
                "If an existing managed LidGuard MCP server is found, mcp-install removes it first and then installs the current command.",
                "With --provider all, only providers whose default configuration roots already exist are processed. Missing providers are reported and skipped."
            ]);
    }
}
