using LidGuard.Commands;
using LidGuard.Ipc;
using LidGuard.Localization;
using LidGuard.Sessions;

namespace LidGuard.Hooks;

internal static class OpenCodeHookCommand
{
    private const string PluginJavaScriptFormat = "plugin-js";

    public static Task<int> RunAsync(string[] commandLineArguments) => new OpenCodeHookCommandRunner().RunAsync(commandLineArguments);

    public static int WriteHookSnippet(string format)
    {
        if (string.IsNullOrWhiteSpace(format)) format = PluginJavaScriptFormat;

        var executablePath = HookCommandUtilities.GetDefaultHookExecutableReference();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            Console.Error.WriteLine(LocalizationService.GetString("HookCommandDefaultExecutableNotResolved"));
            return 1;
        }

        var hookCommand = HookCommandUtilities.CreateHookCommand(executablePath, LidGuardPipeCommands.OpenCodeHook);
        return WriteHookSnippet(format, hookCommand);
    }

    internal static int WriteHookSnippet(string format, string hookCommand)
    {
        if (string.IsNullOrWhiteSpace(format)) format = PluginJavaScriptFormat;
        if (format.Equals(PluginJavaScriptFormat, StringComparison.OrdinalIgnoreCase) || format.Equals("js", StringComparison.OrdinalIgnoreCase) || format.Equals("javascript", StringComparison.OrdinalIgnoreCase))
        {
            Console.Write(OpenCodeHookPluginDocument.CreateManagedPlugin(hookCommand));
            return 0;
        }

        Console.Error.WriteLine(LocalizationService.GetFormattedString("HookCommandUnsupportedSnippetFormat", "OpenCode", "plugin-js or js"));
        return 1;
    }

    private sealed class OpenCodeHookCommandRunner : HookCommandBase<OpenCodeHookInput>
    {
        protected override AgentProvider Provider => AgentProvider.OpenCode;

        public async Task<int> RunAsync(string[] commandLineArguments)
        {
            if (!CommandOptionReader.TryParseOptions(commandLineArguments, 0, out var options, out var optionMessage))
            {
                OpenCodeHookEventLog.AppendMessage(optionMessage);
                return 0;
            }

            var configuredHookEventName = CommandOptionReader.GetOption(options, "event", "event-name").Trim();
            if (string.IsNullOrWhiteSpace(configuredHookEventName))
            {
                OpenCodeHookEventLog.AppendMessage("LidGuard OpenCode hook requires --event <event-name>.");
                return 0;
            }

            var timing = new HookExecutionTiming();
            var readResult = await ReadHookInputAsync(timing, "LidGuard OpenCode hook received empty input.", hookInputJson => ParseHookInput(hookInputJson, configuredHookEventName), message => message);
            if (!readResult.Succeeded) return 0;

            var hookInput = readResult.HookInput;
            timing.AddLogWriteDuration(OpenCodeHookEventLog.AppendReceived(hookInput));
            var hookEventName = hookInput.HookEventName.Trim();
            if (hookEventName.Equals(OpenCodeHookEventNames.ChatMessage, StringComparison.Ordinal)) return await SendRuntimeRequestAsync(LidGuardPipeCommands.Start, hookEventName, hookInput, timing: timing);
            if (hookEventName.Equals(OpenCodeHookEventNames.PermissionAsk, StringComparison.Ordinal)) return await WriteClosedLidPermissionRequestDecisionAsync(hookInput);
            if (OpenCodeHookEventNames.IsActivityEvent(hookEventName)) return await SendSessionStateRequestAsync(LidGuardPipeCommands.MarkSessionActive, hookEventName, hookInput, DescribeActivityReason(hookEventName, hookInput.ToolName));
            if (OpenCodeHookEventNames.IsSoftLockEvent(hookEventName)) return await SendSessionStateRequestAsync(LidGuardPipeCommands.MarkSessionSoftLocked, hookEventName, hookInput, hookEventName);
            if (OpenCodeHookEventNames.IsSoftLockClearEvent(hookEventName)) return await SendSessionStateRequestAsync(LidGuardPipeCommands.MarkSessionActive, hookEventName, hookInput, hookEventName);
            if (OpenCodeHookEventNames.IsStopTrigger(hookEventName, hookInput)) return await SendRuntimeRequestAsync(LidGuardPipeCommands.Stop, hookEventName, hookInput, isProviderSessionEnd: IsNormalSessionEnd(hookEventName, hookInput), timing: timing);

            return 0;
        }

        protected override void AppendMessage(string message) => OpenCodeHookEventLog.AppendMessage(message);

        protected override void AppendRuntimeResult(string hookEventName, OpenCodeHookInput hookInput, string commandName, LidGuardPipeResponse response, string details)
        {
            OpenCodeHookEventLog.AppendRuntimeResult(hookInput, commandName, response.Succeeded, response.RuntimeUnavailable, response.ActiveSessionCount, response.Message, details);
        }

        protected override string CreateSessionEndReason(string hookEventName, OpenCodeHookInput hookInput, string sessionEndReason)
        {
            if (!string.IsNullOrWhiteSpace(sessionEndReason)) return sessionEndReason;
            return string.IsNullOrWhiteSpace(hookEventName) ? "opencode-hook-stop" : hookEventName;
        }

        private static bool IsNormalSessionEnd(string hookEventName, OpenCodeHookInput hookInput)
        {
            if (hookEventName.Equals(OpenCodeHookEventNames.SessionIdle, StringComparison.Ordinal)) return true;
            return hookEventName.Equals(OpenCodeHookEventNames.SessionStatus, StringComparison.Ordinal) && hookInput.SessionStatus.Equals("idle", StringComparison.OrdinalIgnoreCase);
        }

        private static HookCommandInputParseResult<OpenCodeHookInput> ParseHookInput(string hookInputJson, string configuredHookEventName)
        {
            return OpenCodeHookInput.TryParse(hookInputJson, configuredHookEventName, out var hookInput, out var message) ? HookCommandInputParseResult<OpenCodeHookInput>.Success(hookInput) : HookCommandInputParseResult<OpenCodeHookInput>.Failure($"LidGuard OpenCode hook could not parse input: {message}");
        }

        private Task<int> WriteClosedLidPermissionRequestDecisionAsync(OpenCodeHookInput hookInput)
        {
            return WriteClosedLidDecisionAsync(OpenCodeHookEventNames.PermissionAsk, hookInput, response => $"LidGuard OpenCode hook skipped permission decision because runtime status is unavailable: {response.Message}", response => $"LidGuard OpenCode hook left permission handling to OpenCode because {ClosedLidPolicyStatus.DescribeInactiveReason(response)}.", response => $"LidGuard OpenCode hook handled closed-lid permission request with {response.Settings.ClosedLidPermissionRequestDecision}.", response => OpenCodeClosedLidPermissionRequestDecisionOutput.Write(response.Settings), true);
        }
    }
}
