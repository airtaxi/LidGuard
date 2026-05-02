using LidGuard.Mcp;

namespace LidGuard.Commands.Help;

internal static class ProviderMcpServerHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            ProviderMcpServerCommand.CommandName,
            [],
            LidGuardHelpSectionTitles.ManagedAndInternalCommands,
            $"{commandDisplayName} {ProviderMcpServerCommand.CommandName} --provider-name <name>",
            "Host the dedicated provider MCP stdio server for a single caller-supplied provider name.",
            [
                new LidGuardHelpOption("--provider-name <name>", "Required provider name exposed to the provider MCP tools.")
            ],
            []);
    }
}
