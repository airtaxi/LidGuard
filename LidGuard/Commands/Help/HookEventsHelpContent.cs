using LidGuard.Ipc;
using LidGuard.Localization;

namespace LidGuard.Commands.Help;

internal static class HookEventsHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            LidGuardPipeCommands.HookEvents,
            [],
            LidGuardHelpSectionTitles.HookIntegration,
            $"{commandDisplayName} hook-events [--provider codex|claude|copilot|all] [--count <number>]",
            LocalizationService.GetString("Help_HookEvents_Description"),
            [
                new LidGuardHelpOption("--provider <provider>", LocalizationService.GetString("Help_ManagedProvider_ProviderOption")),
                new LidGuardHelpOption("--count <number>", LocalizationService.GetString("Help_HookEvents_CountOption"))
            ],
            [
                LocalizationService.GetString("Help_ManagedProvider_AllProvidersNote")
            ]);
    }
}
