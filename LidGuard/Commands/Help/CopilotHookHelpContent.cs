using LidGuard.Ipc;

namespace LidGuard.Commands.Help;

internal static class CopilotHookHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            LidGuardPipeCommands.CopilotHook,
            [],
            LidGuardHelpSectionTitles.ManagedAndInternalCommands,
            $"{commandDisplayName} copilot-hook --event <event-name>",
            "Read GitHub Copilot CLI hook JSON from standard input for one configured event name.",
            [
                new LidGuardHelpOption("--event <event-name>", "Required. Typical values include sessionStart, sessionEnd, userPromptSubmitted, preToolUse, postToolUse, permissionRequest, agentStop, errorOccurred, and notification.")
            ],
            []);
    }
}
