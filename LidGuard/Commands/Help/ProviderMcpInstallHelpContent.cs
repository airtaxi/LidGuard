using LidGuard.Ipc;
using LidGuard.Localization;

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
            LocalizationService.GetString("Help_ProviderMcpInstall_Description"),
            [
                new LidGuardHelpOption("--config <json-path>", LocalizationService.GetString("Help_ProviderMcpInstall_ConfigOption")),
                new LidGuardHelpOption("--provider-name <name>", LocalizationService.GetString("Help_ProviderMcpInstall_ProviderNameOption")),
                new LidGuardHelpOption("--server-name <name>", LocalizationService.GetString("Help_ProviderMcp_ServerNameOption"))
            ],
            [
                LocalizationService.GetString("Help_ProviderMcpInstall_DirectEditNote")
            ]);
    }
}
