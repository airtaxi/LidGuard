using LidGuard.Ipc;

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
            "Remove active sessions currently tracked by the runtime.",
            [
                new LidGuardHelpCommand(
                    $"{commandDisplayName} remove-session --all",
                    "Remove every active session currently tracked by the runtime.",
                    [],
                    [
                        "--all cannot be combined with --session, --provider, or --provider-name."
                    ]),
                new LidGuardHelpCommand(
                    $"{commandDisplayName} remove-session --session <id> [--provider codex|claude|copilot|custom|mcp|unknown] [--provider-name <name>]",
                    "Remove active sessions by session identifier without waiting for provider stop hooks.",
                    [
                        new LidGuardHelpOption("--session <id>", "Required. Session identifier to remove."),
                        new LidGuardHelpOption("--provider <provider>", "Optional. Narrows removal to one provider. Allowed values: codex, claude, copilot, custom, mcp, or unknown."),
                        new LidGuardHelpOption("--provider-name <name>", "Optional. Narrows removal to one MCP provider name when --provider mcp is used.")
                    ],
                    [
                        "When --provider is omitted, LidGuard removes every active session whose session identifier matches.",
                        "When --provider mcp is used without --provider-name, LidGuard removes every MCP-backed session with the same session identifier."
                    ])
            ]);
    }
}
