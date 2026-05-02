using LidGuard.Ipc;

namespace LidGuard.Commands.Help;

internal static class StopHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            LidGuardPipeCommands.Stop,
            [],
            LidGuardHelpSectionTitles.SessionControl,
            $"{commandDisplayName} stop --provider codex|claude|copilot|custom|mcp [--session <id>] [--provider-name <name>] [--parent-pid <pid>] [--working-directory <path>]",
            "Stop a tracked session by matching the same provider and session identity used when the session started.",
            [
                new LidGuardHelpOption("--provider <provider>", "Required. Allowed values: codex, claude, copilot, custom, or mcp."),
                new LidGuardHelpOption("--session <id>", "Optional. When omitted, LidGuard uses the same fallback session identifier strategy as start."),
                new LidGuardHelpOption("--provider-name <name>", "Required when --provider mcp is used."),
                new LidGuardHelpOption("--parent-pid <pid>", "Optional non-negative watched process identifier."),
                new LidGuardHelpOption("--working-directory <path>", "Optional working directory used for fallback session identity. Defaults to the current directory.")
            ],
            []);
    }
}
