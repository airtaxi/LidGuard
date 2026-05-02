using LidGuard.Ipc;

namespace LidGuard.Commands.Help;

internal static class CodexHookHelpContent
{
    internal static LidGuardHelpCommandEntry Create(LidGuardHelpDocumentContext context)
    {
        var commandDisplayName = context.CommandDisplayName;
        return LidGuardHelpCommandEntryFactory.CreateSingleCommandEntry(
            LidGuardPipeCommands.CodexHook,
            [],
            LidGuardHelpSectionTitles.ManagedAndInternalCommands,
            $"{commandDisplayName} codex-hook",
            "Read Codex hook JSON from standard input and forward start, stop, or closed-lid permission decisions to the runtime.",
            [],
            []);
    }
}
