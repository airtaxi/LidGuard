using LidGuard.Mcp;
using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class ProviderMcpServerHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(ProviderMcpServerCommand.CommandName, [], LidGuardHelpSectionTitles.ManagedAndInternalCommands, $"{commandDisplayName} {ProviderMcpServerCommand.CommandName} --provider-name <name>", LocalizationService.GetString("Help_ProviderMcpServer_Description"), [new LidGuardHelpOption("--provider-name <name>", LocalizationService.GetString("Help_ProviderMcpServer_ProviderNameOption"))], []);
    }
}
