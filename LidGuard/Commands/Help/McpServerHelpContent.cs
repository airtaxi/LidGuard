using LidGuard.Mcp;
using LidGuard.Localization;

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
            LocalizationService.GetString("Help_McpServer_Description"),
            [],
            []);
    }
}
