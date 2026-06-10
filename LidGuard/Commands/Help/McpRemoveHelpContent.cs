using LidGuard.Ipc;
using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class McpRemoveHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(LidGuardPipeCommands.McpRemove, ["mcp-uninstall"], LidGuardHelpSectionTitles.McpIntegration, $"{commandDisplayName} mcp-remove [codex|claude|copilot|opencode|all]", LocalizationService.GetString("Help_McpRemove_Description"), [new LidGuardHelpOption("<provider>", LocalizationService.GetString("Help_Mcp_ProviderArgument"))], [LocalizationService.GetString("Help_ManagedProvider_AllProvidersArgumentNote")]);
    }
}
