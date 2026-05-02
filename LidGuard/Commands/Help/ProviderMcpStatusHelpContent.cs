using LidGuard.Ipc;

namespace LidGuard.Commands.Help;

internal static class ProviderMcpStatusHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            LidGuardPipeCommands.ProviderMcpStatus,
            [],
            LidGuardHelpSectionTitles.McpIntegration,
            $"{commandDisplayName} {LidGuardPipeCommands.ProviderMcpStatus} --config <json-path> [--server-name <name>]",
            "Inspect a caller-supplied JSON configuration file for a managed provider MCP server entry.",
            [
                new LidGuardHelpOption("--config <json-path>", "Required. JSON configuration file to inspect."),
                new LidGuardHelpOption("--server-name <name>", "Optional managed server entry name. Defaults to lidguard-provider.")
            ],
            []);
    }
}
