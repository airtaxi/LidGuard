using LidGuard.Ipc;

namespace LidGuard.Commands.Help;

internal static class ProviderMcpRemoveHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            LidGuardPipeCommands.ProviderMcpRemove,
            ["provider-mcp-uninstall"],
            LidGuardHelpSectionTitles.McpIntegration,
            $"{commandDisplayName} {LidGuardPipeCommands.ProviderMcpRemove} --config <json-path> [--server-name <name>]",
            "Remove a managed provider MCP server entry from a caller-supplied JSON configuration file.",
            [
                new LidGuardHelpOption("--config <json-path>", "Required. JSON configuration file to update."),
                new LidGuardHelpOption("--server-name <name>", "Optional managed server entry name. Defaults to lidguard-provider.")
            ],
            []);
    }
}
