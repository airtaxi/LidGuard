using LidGuard.Ipc;

namespace LidGuard.Commands.Help;

internal static class ClaudeHookHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            LidGuardPipeCommands.ClaudeHook,
            [],
            LidGuardHelpSectionTitles.ManagedAndInternalCommands,
            $"{commandDisplayName} claude-hook",
            "Read Claude Code hook JSON from standard input and forward start, stop, activity, soft-lock, elicitation, or permission decisions to the runtime.",
            [],
            []);
    }
}
