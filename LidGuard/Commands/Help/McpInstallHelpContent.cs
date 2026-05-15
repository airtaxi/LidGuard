using LidGuard.Ipc;
using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class McpInstallHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            LidGuardPipeCommands.McpInstall,
            [],
            LidGuardHelpSectionTitles.McpIntegration,
            $"{commandDisplayName} mcp-install [codex|claude|copilot|all]",
            LocalizationService.GetString("Help_McpInstall_Description"),
            [
                new LidGuardHelpOption("<provider>", LocalizationService.GetString("Help_Mcp_ProviderArgument"))
            ],
            [
                LocalizationService.GetString("Help_McpInstall_RefreshNote"),
                LocalizationService.GetString("Help_ManagedProvider_AllProvidersArgumentNote")
            ]);
    }
}
