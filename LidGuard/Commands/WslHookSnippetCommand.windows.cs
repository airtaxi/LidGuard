using LidGuard.Hooks;
using LidGuard.Ipc;
using LidGuard.Localization;
using LidGuard.Sessions;

namespace LidGuard.Commands;

internal static class WslHookSnippetCommand
{
    public static bool IsCommandName(string commandName) => commandName is LidGuardPipeCommands.WslCodexHooks or LidGuardPipeCommands.WslClaudeHooks or LidGuardPipeCommands.WslCopilotHooks;

    public static int WriteHookSnippet(string commandName, string[] commandLineArguments)
    {
        if (!TryParseArguments(commandLineArguments, out var format, out var options, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        if (!WslCommandUtilities.TryGetDistroName(options, out var distroName, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        if (!WslCommandUtilities.TryValidateWsl(distroName, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        if (!WslCommandUtilities.TryGetWslLidGuardExecutablePath(distroName, out var wslExecutablePath, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var provider = GetProvider(commandName);
        var hookCommandName = WslCommandUtilities.GetHookCommandName(provider);
        var hookCommand = WslCommandUtilities.CreateWslLidGuardCommand(wslExecutablePath, hookCommandName);

        return provider switch
        {
            AgentProvider.Codex => CodexHookCommand.WriteHookSnippet(format, hookCommand),
            AgentProvider.Claude => ClaudeHookCommand.WriteHookSnippet(format, hookCommand, HookCommandUtilities.BashShellName),
            AgentProvider.GitHubCopilot => GitHubCopilotHookCommand.WriteHookSnippet(format, hookCommand, HookCommandUtilities.BashShellName),
            _ => WriteUnsupportedProvider()
        };
    }

    private static bool TryParseArguments(string[] commandLineArguments, out string format, out Dictionary<string, string> options, out string message)
    {
        format = string.Empty;
        options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        message = string.Empty;

        for (var argumentIndex = 0; argumentIndex < commandLineArguments.Length; argumentIndex++)
        {
            var argument = commandLineArguments[argumentIndex];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(format))
                {
                    message = LocalizationService.GetFormattedString("CommandUnexpectedArgument", argument);
                    return false;
                }

                format = argument;
                continue;
            }

            var separatorIndex = argument.IndexOf('=');
            if (separatorIndex > 2)
            {
                options[argument[2..separatorIndex]] = argument[(separatorIndex + 1)..];
                continue;
            }

            var optionName = argument[2..];
            if (string.IsNullOrWhiteSpace(optionName))
            {
                message = LocalizationService.GetString("CommandOptionNameRequired");
                return false;
            }

            if (!optionName.Equals(WslCommandUtilities.DistroOptionName, StringComparison.OrdinalIgnoreCase))
            {
                message = LocalizationService.GetFormattedString("CommandUnexpectedArgument", argument);
                return false;
            }

            if (argumentIndex + 1 >= commandLineArguments.Length || commandLineArguments[argumentIndex + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options[optionName] = bool.TrueString;
                continue;
            }

            options[optionName] = commandLineArguments[++argumentIndex];
        }

        return true;
    }

    private static AgentProvider GetProvider(string commandName)
    {
        return commandName switch
        {
            LidGuardPipeCommands.WslCodexHooks => AgentProvider.Codex,
            LidGuardPipeCommands.WslClaudeHooks => AgentProvider.Claude,
            LidGuardPipeCommands.WslCopilotHooks => AgentProvider.GitHubCopilot,
            _ => AgentProvider.Unknown
        };
    }

    private static int WriteUnsupportedProvider()
    {
        Console.Error.WriteLine(LocalizationService.GetString("ManagementUnsupportedHookManagement"));
        return 1;
    }
}
