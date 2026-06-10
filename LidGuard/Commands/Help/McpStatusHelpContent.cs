using LidGuard.Ipc;
using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class McpStatusHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(LidGuardPipeCommands.McpStatus, [], LidGuardHelpSectionTitles.McpIntegration, $"{commandDisplayName} mcp-status [codex|claude|copilot|opencode|all]", LocalizationService.GetString("Help_McpStatus_Description"), [new LidGuardHelpOption("<provider>", LocalizationService.GetString("Help_Mcp_ProviderArgument"))], [LocalizationService.GetString("Help_ManagedProvider_AllProvidersArgumentNote")]);
    }
}
