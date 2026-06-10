using LidGuard.Ipc;
using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class StartHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(LidGuardPipeCommands.Start, [], LidGuardHelpSectionTitles.SessionControl, $"{commandDisplayName} start --provider codex|claude|copilot|opencode|custom|mcp [--session <id>] [--provider-name <name>] [--parent-pid <pid>] [--working-directory <path>]", LocalizationService.GetString("Help_Start_Description"), [new LidGuardHelpOption("--provider <provider>", LocalizationService.GetString("Help_Session_ProviderOption")), new LidGuardHelpOption("--session <id>", LocalizationService.GetString("Help_Start_SessionOption")), new LidGuardHelpOption("--provider-name <name>", LocalizationService.GetString("Help_Start_ProviderNameOption")), new LidGuardHelpOption("--parent-pid <pid>", LocalizationService.GetString("Help_Start_ParentProcessOption")), new LidGuardHelpOption("--working-directory <path>", LocalizationService.GetString("Help_Start_WorkingDirectoryOption"))], [LocalizationService.GetString("Help_Start_RuntimeLaunchNote")]);
    }
}
