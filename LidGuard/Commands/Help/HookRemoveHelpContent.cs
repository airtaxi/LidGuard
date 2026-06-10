using LidGuard.Ipc;
using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class HookRemoveHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(LidGuardPipeCommands.HookRemove, ["hook-uninstall"], LidGuardHelpSectionTitles.HookIntegration, $"{commandDisplayName} hook-remove [--provider codex|claude|copilot|opencode|all] [--config <path>]", LocalizationService.GetString("Help_HookRemove_Description"), [new LidGuardHelpOption("--provider <provider>", LocalizationService.GetString("Help_ManagedProvider_ProviderOption")), new LidGuardHelpOption("--config <path>", LocalizationService.GetString("Help_ManagedProvider_ConfigOption"))], [LocalizationService.GetString("Help_ManagedProvider_ConfigAllNote"), LocalizationService.GetString("Help_ManagedProvider_AllProvidersNote")]);
    }
}
