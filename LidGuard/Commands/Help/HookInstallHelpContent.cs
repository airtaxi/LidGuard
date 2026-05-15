using LidGuard.Ipc;
using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class HookInstallHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            LidGuardPipeCommands.HookInstall,
            [],
            LidGuardHelpSectionTitles.HookIntegration,
            $"{commandDisplayName} hook-install [--provider codex|claude|copilot|all] [--config <path>]",
            LocalizationService.GetString("Help_HookInstall_Description"),
            [
                new LidGuardHelpOption("--provider <provider>", LocalizationService.GetString("Help_ManagedProvider_ProviderOption")),
                new LidGuardHelpOption("--config <path>", LocalizationService.GetString("Help_ManagedProvider_ConfigOption"))
            ],
            [
                LocalizationService.GetString("Help_ManagedProvider_ConfigAllNote"),
                LocalizationService.GetString("Help_ManagedProvider_AllProvidersNote")
            ]);
    }
}
