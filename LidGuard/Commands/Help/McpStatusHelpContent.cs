using LidGuard.Ipc;

namespace LidGuard.Commands.Help;

internal static class McpStatusHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            LidGuardPipeCommands.McpStatus,
            [],
            LidGuardHelpSectionTitles.McpIntegration,
            $"{commandDisplayName} mcp-status [--provider codex|claude|copilot|all]",
            "Inspect the managed user/global LidGuard MCP server registration for one provider or every detected provider.",
            [
                new LidGuardHelpOption("--provider <provider>", "Optional. Allowed values: codex, claude, copilot, or all. When omitted, LidGuard prompts for a provider.")
            ],
            [
                "With --provider all, only providers whose default configuration roots already exist are processed. Missing providers are reported and skipped."
            ]);
    }
}
