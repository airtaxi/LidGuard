using LidGuard.Ipc;
using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class RemoveSessionHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return new LidGuardHelpCommandEntry(
            LidGuardPipeCommands.RemoveSession,
            [],
            LidGuardHelpSectionTitles.SessionControl,
            LocalizationService.GetString("Help_RemoveSession_Description"),
            [
                new LidGuardHelpCommand(
                    $"{commandDisplayName} remove-session --all",
                    LocalizationService.GetString("Help_RemoveSession_AllDescription"),
                    [],
                    [
                        LocalizationService.GetString("Help_RemoveSession_AllCannotCombineNote")
                    ]),
                new LidGuardHelpCommand(
                    $"{commandDisplayName} remove-session --session <id> [--provider codex|claude|copilot|custom|mcp|unknown] [--provider-name <name>]",
                    LocalizationService.GetString("Help_RemoveSession_ByIdentifierDescription"),
                    [
                        new LidGuardHelpOption("--session <id>", LocalizationService.GetString("Help_RemoveSession_SessionOption")),
                        new LidGuardHelpOption("--provider <provider>", LocalizationService.GetString("Help_RemoveSession_ProviderOption")),
                        new LidGuardHelpOption("--provider-name <name>", LocalizationService.GetString("Help_RemoveSession_ProviderNameOption"))
                    ],
                    [
                        LocalizationService.GetString("Help_RemoveSession_NoProviderNote"),
                        LocalizationService.GetString("Help_RemoveSession_McpWithoutProviderNameNote")
                    ])
            ]);
    }
}
