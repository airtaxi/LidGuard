using LidGuard.Ipc;

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
            "Print recent hook event log lines for the selected provider or providers.",
            [
                new LidGuardHelpOption("--provider <provider>", "Optional. Allowed values: codex, claude, copilot, or all. When omitted, LidGuard prompts for a provider."),
                new LidGuardHelpOption("--count <number>", "Optional positive line count. Defaults to 50.")
            ],
            [
                "With --provider all, only providers whose default configuration roots already exist are processed. Missing providers are reported and skipped."
            ]);
    }
}
