using LidGuard.Ipc;

namespace LidGuard.Commands.Help;

internal static class ProviderMcpInstallHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            LidGuardPipeCommands.ProviderMcpInstall,
            [],
            LidGuardHelpSectionTitles.McpIntegration,
            $"{commandDisplayName} {LidGuardPipeCommands.ProviderMcpInstall} --config <json-path> --provider-name <name> [--server-name <name>]",
            "Write or update a managed provider MCP stdio server entry in a caller-supplied JSON configuration file.",
            [
                new LidGuardHelpOption("--config <json-path>", "Required. JSON configuration file to create or update."),
                new LidGuardHelpOption("--provider-name <name>", "Required provider name passed through to provider-mcp-server."),
                new LidGuardHelpOption("--server-name <name>", "Optional managed server entry name. Defaults to lidguard-provider.")
            ],
            [
                "This path edits the supplied JSON file directly and does not call provider-specific mcp add/remove commands."
            ]);
    }
}
