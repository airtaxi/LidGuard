using LidGuard.Ipc;

namespace LidGuard.Commands.Help;

internal static class StartHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            LidGuardPipeCommands.Start,
            [],
            LidGuardHelpSectionTitles.SessionControl,
            $"{commandDisplayName} start --provider codex|claude|copilot|custom|mcp [--session <id>] [--provider-name <name>] [--parent-pid <pid>] [--working-directory <path>]",
            "Start or refresh a tracked session and load persisted default settings into the runtime request.",
            [
                new LidGuardHelpOption("--provider <provider>", "Required. Allowed values: codex, claude, copilot, custom, or mcp."),
                new LidGuardHelpOption("--session <id>", "Optional. Session identifier to track. When omitted, LidGuard derives one from the provider display name and normalized working directory."),
                new LidGuardHelpOption("--provider-name <name>", "Required when --provider mcp is used. Distinguishes one MCP-backed provider from another."),
                new LidGuardHelpOption("--parent-pid <pid>", "Optional non-negative watched process identifier used by the runtime watchdog."),
                new LidGuardHelpOption("--working-directory <path>", "Optional working directory used for fallback session identity and process resolution. Defaults to the current directory.")
            ],
            [
                "If no runtime is listening, start launches the detached runtime server automatically."
            ]);
    }
}
