using LidGuard.Ipc;

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
            "Install the managed provider hook entries into the selected configuration file.",
            [
                new LidGuardHelpOption("--provider <provider>", "Optional. Allowed values: codex, claude, copilot, or all. When omitted, LidGuard prompts for a provider."),
                new LidGuardHelpOption("--config <path>", "Optional provider-specific configuration file override.")
            ],
            [
                "Do not combine --config with --provider all because each provider uses a different configuration file.",
                "With --provider all, only providers whose default configuration roots already exist are processed. Missing providers are reported and skipped."
            ]);
    }
}
