using LidGuard.Ipc;
using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class ProviderMcpRemoveHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(LidGuardPipeCommands.ProviderMcpRemove, ["provider-mcp-uninstall"], LidGuardHelpSectionTitles.McpIntegration, $"{commandDisplayName} {LidGuardPipeCommands.ProviderMcpRemove} --config <json-path> [--server-name <name>]", LocalizationService.GetString("Help_ProviderMcpRemove_Description"), [new LidGuardHelpOption("--config <json-path>", LocalizationService.GetString("Help_ProviderMcpRemove_ConfigOption")), new LidGuardHelpOption("--server-name <name>", LocalizationService.GetString("Help_ProviderMcp_ServerNameOption"))], []);
    }
}
