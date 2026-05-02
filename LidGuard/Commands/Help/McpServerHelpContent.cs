using LidGuard.Mcp;

namespace LidGuard.Commands.Help;

internal static class McpServerHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            LidGuardMcpServerCommand.CommandName,
            [],
            LidGuardHelpSectionTitles.ManagedAndInternalCommands,
            $"{commandDisplayName} {LidGuardMcpServerCommand.CommandName}",
            "Host the regular LidGuard stdio MCP server that exposes settings and session management tools.",
            [],
            []);
    }
}
