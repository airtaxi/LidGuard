using System.Text.Json;
using LidGuard.Ipc;
using LidGuard.Localization;
using LidGuard.Sessions;

namespace LidGuard.Hooks;

internal static class CodexHookCommand
{
    private const string ConfigTomlFormat = "config-toml";
    private const string HooksJsonFormat = "hooks-json";

    public static Task<int> RunAsync() => new CodexHookCommandRunner().RunAsync();

    public static int WriteHookSnippet(string format)
    {
        if (string.IsNullOrWhiteSpace(format)) format = ConfigTomlFormat;

        var executablePath = HookCommandUtilities.GetDefaultHookExecutableReference();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            Console.Error.WriteLine("A default LidGuard hook executable or command name could not be resolved.");
            return 1;
        }

        var hookCommand = HookCommandUtilities.CreateHookCommand(executablePath, LidGuardPipeCommands.CodexHook);

        if (format.Equals(ConfigTomlFormat, StringComparison.OrdinalIgnoreCase) || format.Equals("toml", StringComparison.OrdinalIgnoreCase))
        {
            WriteConfigTomlSnippet(hookCommand);
            return 0;
        }

        if (format.Equals(HooksJsonFormat, StringComparison.OrdinalIgnoreCase) || format.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            WriteHooksJsonSnippet(hookCommand);
            return 0;
        }

        Console.Error.WriteLine("Unsupported Codex hook snippet format. Use config-toml or hooks-json.");
        return 1;
    }

    private sealed class CodexHookCommandRunner : HookCommandBase<CodexHookInput>
    {
        protected override AgentProvider Provider => AgentProvider.Codex;

        public async Task<int> RunAsync()
        {
            var timing = new HookExecutionTiming();
            var readResult = await ReadHookInputAsync(
                timing,
                "LidGuard Codex hook received empty input.",
                ParseHookInput,
                message => message);
            if (!readResult.Succeeded) return 0;

            var hookInput = readResult.HookInput;
            timing.AddLogWriteDuration(CodexHookEventLog.AppendReceived(hookInput));
            var hookEventName = hookInput.HookEventName.Trim();
            if (hookEventName.Equals(CodexHookEventNames.UserPromptSubmit, StringComparison.Ordinal)) return await SendRuntimeRequestAsync(LidGuardPipeCommands.Start, hookEventName, hookInput, timing: timing);
            if (hookEventName.Equals(CodexHookEventNames.PermissionRequest, StringComparison.Ordinal)) return await WriteClosedLidPermissionRequestDecisionAsync();
            if (CodexHookEventNames.IsStopTrigger(hookEventName)) return await SendRuntimeRequestAsync(LidGuardPipeCommands.Stop, hookEventName, hookInput, isProviderSessionEnd: true, timing: timing);

            return 0;
        }

        protected override void AppendMessage(string message) => CodexHookEventLog.AppendMessage(message);

        protected override void AppendRuntimeResult(
            string hookEventName,
            CodexHookInput hookInput,
            string commandName,
            LidGuardPipeResponse response,
            string details)
        {
            CodexHookEventLog.AppendRuntimeResult(
                hookInput,
                commandName,
                response.Succeeded,
                response.RuntimeUnavailable,
                response.ActiveSessionCount,
                response.Message,
                details);
        }

        protected override string CreateSessionEndReason(string hookEventName, CodexHookInput hookInput, string sessionEndReason)
        {
            if (!string.IsNullOrWhiteSpace(sessionEndReason)) return sessionEndReason;
            if (string.IsNullOrWhiteSpace(hookInput.HookEventName)) return "codex-hook-stop";
            return hookInput.HookEventName;
        }

        private static HookCommandInputParseResult<CodexHookInput> ParseHookInput(string hookInputJson)
        {
            try
            {
                var hookInput = JsonSerializer.Deserialize(hookInputJson, LidGuardJsonSerializerContext.Default.CodexHookInput);
                return hookInput is null
                    ? HookCommandInputParseResult<CodexHookInput>.Failure("LidGuard Codex hook could not parse input.")
                    : HookCommandInputParseResult<CodexHookInput>.Success(hookInput);
            }
            catch (JsonException exception)
            {
                return HookCommandInputParseResult<CodexHookInput>.Failure($"LidGuard Codex hook could not parse input: {exception.Message}");
            }
        }

        private Task<int> WriteClosedLidPermissionRequestDecisionAsync()
        {
            return WriteClosedLidDecisionAsync(
                response => $"LidGuard Codex hook skipped PermissionRequest decision because runtime status is unavailable: {response.Message}",
                response => $"LidGuard Codex hook left PermissionRequest to Codex because {ClosedLidPolicyStatus.DescribeInactiveReason(response)}.",
                response => $"LidGuard Codex hook handled closed-lid PermissionRequest with {response.Settings.ClosedLidPermissionRequestDecision}.",
                response => CodexClosedLidPermissionRequestDecisionOutput.Write(response.Settings));
        }
    }

    private static void WriteConfigTomlSnippet(string hookCommand) => Console.WriteLine(CodexHookConfigTomlDocument.InstallManagedHookBlock(string.Empty, hookCommand).TrimEnd());

    private static void WriteHooksJsonSnippet(string hookCommand)
    {
        var jsonCommandLiteral = CodexHookConfigTomlDocument.ToJsonStringLiteral(hookCommand);
        var hookBlockDefinitions = new (string HookEventName, string StatusMessage)[]
        {
            (CodexHookEventNames.UserPromptSubmit, LocalizationService.GetString("HookStatusMessageStartingTurnProtection")),
            (CodexHookEventNames.PermissionRequest, LocalizationService.GetString("HookStatusMessageRespondingToClosedLidPermissionRequest")),
            (CodexHookEventNames.Stop, LocalizationService.GetString("HookStatusMessageStoppingSessionProtection"))
        };

        Console.WriteLine("{");
        Console.WriteLine("  \"hooks\": {");
        for (var index = 0; index < hookBlockDefinitions.Length; index++)
        {
            var hookBlockDefinition = hookBlockDefinitions[index];
            WriteHooksJsonHookBlock(hookBlockDefinition.HookEventName, jsonCommandLiteral, hookBlockDefinition.StatusMessage, index < hookBlockDefinitions.Length - 1);
        }

        Console.WriteLine("  }");
        Console.WriteLine("}");
    }

    private static void WriteHooksJsonHookBlock(string hookEventName, string jsonCommandLiteral, string statusMessage, bool hasTrailingComma)
    {
        Console.WriteLine($"    \"{hookEventName}\": [");
        Console.WriteLine("      {");
        Console.WriteLine("        \"hooks\": [");
        Console.WriteLine("          {");
        Console.WriteLine("            \"type\": \"command\",");
        Console.WriteLine($"            \"command\": {jsonCommandLiteral},");
        Console.WriteLine("            \"timeout\": 30,");
        Console.WriteLine($"            \"statusMessage\": {CodexHookConfigTomlDocument.ToJsonStringLiteral(statusMessage)}");
        Console.WriteLine("          }");
        Console.WriteLine("        ]");
        Console.WriteLine("      }");
        Console.WriteLine(hasTrailingComma ? "    ]," : "    ]");
    }

}

