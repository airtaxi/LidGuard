using LidGuard.Ipc;
using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class ProviderMcpStatusHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(LidGuardPipeCommands.ProviderMcpStatus, [], LidGuardHelpSectionTitles.McpIntegration, $"{commandDisplayName} {LidGuardPipeCommands.ProviderMcpStatus} --config <json-path> [--server-name <name>]", LocalizationService.GetString("Help_ProviderMcpStatus_Description"), [new LidGuardHelpOption("--config <json-path>", LocalizationService.GetString("Help_ProviderMcpStatus_ConfigOption")), new LidGuardHelpOption("--server-name <name>", LocalizationService.GetString("Help_ProviderMcp_ServerNameOption"))], []);
    }
}
