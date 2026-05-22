using LidGuard.Ipc;
using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class StopHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(LidGuardPipeCommands.Stop, [], LidGuardHelpSectionTitles.SessionControl, $"{commandDisplayName} stop --provider codex|claude|copilot|custom|mcp [--session <id>] [--provider-name <name>] [--parent-pid <pid>] [--working-directory <path>]", LocalizationService.GetString("Help_Stop_Description"), [new LidGuardHelpOption("--provider <provider>", LocalizationService.GetString("Help_Session_ProviderOption")), new LidGuardHelpOption("--session <id>", LocalizationService.GetString("Help_Stop_SessionOption")), new LidGuardHelpOption("--provider-name <name>", LocalizationService.GetString("Help_Stop_ProviderNameOption")), new LidGuardHelpOption("--parent-pid <pid>", LocalizationService.GetString("Help_Stop_ParentProcessOption")), new LidGuardHelpOption("--working-directory <path>", LocalizationService.GetString("Help_Stop_WorkingDirectoryOption"))], []);
    }
}
