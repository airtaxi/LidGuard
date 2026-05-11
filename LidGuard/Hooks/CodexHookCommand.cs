using System.Diagnostics;
using System.Text.Json;
using LidGuard.Ipc;
using LidGuard.Localization;
using LidGuard.Settings;
using LidGuard.Hooks;
using LidGuard.Sessions;

namespace LidGuard.Hooks;

internal static class CodexHookCommand
{
    private const string ConfigTomlFormat = "config-toml";
    private const string HooksJsonFormat = "hooks-json";

    public static async Task<int> RunAsync()
    {
        var timing = new HookExecutionTiming();
        var inputReadStopwatch = Stopwatch.StartNew();
        var hookInputJson = await Console.In.ReadToEndAsync();
        inputReadStopwatch.Stop();
        timing.InputReadDuration = inputReadStopwatch.Elapsed;
        if (string.IsNullOrWhiteSpace(hookInputJson))
        {
            CodexHookEventLog.AppendMessage("LidGuard Codex hook received empty input.");
            return 0;
        }

        CodexHookInput hookInput;
        var parseStopwatch = Stopwatch.StartNew();
        try
        {
            hookInput = JsonSerializer.Deserialize(hookInputJson, LidGuardJsonSerializerContext.Default.CodexHookInput);
        }
        catch (JsonException exception)
        {
            parseStopwatch.Stop();
            timing.ParseDuration = parseStopwatch.Elapsed;
            CodexHookEventLog.AppendMessage($"LidGuard Codex hook could not parse input: {exception.Message}");
            return 0;
        }
        parseStopwatch.Stop();
        timing.ParseDuration = parseStopwatch.Elapsed;

        if (hookInput is null)
        {
            CodexHookEventLog.AppendMessage("LidGuard Codex hook could not parse input.");
            return 0;
        }

        timing.AddLogWriteDuration(CodexHookEventLog.AppendReceived(hookInput));
        var hookEventName = hookInput.HookEventName.Trim();
        if (hookEventName.Equals(CodexHookEventNames.UserPromptSubmit, StringComparison.Ordinal)) return await SendRuntimeRequestAsync(LidGuardPipeCommands.Start, hookInput, timing);
        if (hookEventName.Equals(CodexHookEventNames.PermissionRequest, StringComparison.Ordinal)) return await WriteClosedLidPermissionRequestDecisionAsync();
        if (CodexHookEventNames.IsStopTrigger(hookEventName)) return await SendRuntimeRequestAsync(LidGuardPipeCommands.Stop, hookInput, timing);

        return 0;
    }

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

    private static async Task<int> WriteClosedLidPermissionRequestDecisionAsync()
    {
        var response = await new LidGuardRuntimeClient().SendAsync(new LidGuardPipeRequest { Command = LidGuardPipeCommands.Status }, false);
        if (!response.Succeeded)
        {
            CodexHookEventLog.AppendMessage($"LidGuard Codex hook skipped PermissionRequest decision because runtime status is unavailable: {response.Message}");
            return 0;
        }

        if (!ClosedLidPolicyStatus.IsActive(response))
        {
            CodexHookEventLog.AppendMessage(
                $"LidGuard Codex hook left PermissionRequest to Codex because {ClosedLidPolicyStatus.DescribeInactiveReason(response)}.");
            return 0;
        }

        CodexHookEventLog.AppendMessage($"LidGuard Codex hook handled closed-lid PermissionRequest with {response.Settings.ClosedLidPermissionRequestDecision}.");
        return CodexClosedLidPermissionRequestDecisionOutput.Write(response.Settings);
    }

    private static async Task<int> SendRuntimeRequestAsync(string commandName, CodexHookInput hookInput, HookExecutionTiming timing)
    {
        // codex-rs hook handling accepts exit 0 + empty stdout as a no-op success,
        // while non-empty stdout can be interpreted differently per event.
        var hasSettings = false;
        var settings = LidGuardSettings.Default;
        var settingsLoadStopwatch = Stopwatch.StartNew();
        if (commandName == LidGuardPipeCommands.Start)
        {
            if (!LidGuardSettingsStore.TryLoadOrCreate(out settings, out var settingsMessage))
            {
                settingsLoadStopwatch.Stop();
                timing.SettingsLoadDuration = settingsLoadStopwatch.Elapsed;
                CodexHookEventLog.AppendMessage(settingsMessage);
                return 0;
            }

            hasSettings = true;
        }
        settingsLoadStopwatch.Stop();
        timing.SettingsLoadDuration = settingsLoadStopwatch.Elapsed;

        var parentProcessResolveStopwatch = Stopwatch.StartNew();
        var watchedProcessIdentifier = HookCommandUtilities.ResolveHookWatchedProcessIdentifier(commandName, AgentProvider.Codex, settings);
        parentProcessResolveStopwatch.Stop();
        timing.ParentProcessResolveDuration = parentProcessResolveStopwatch.Elapsed;

        var request = new LidGuardPipeRequest
        {
            Command = commandName,
            Provider = AgentProvider.Codex,
            SessionIdentifier = GetSessionIdentifier(hookInput),
            IsProviderSessionEnd = commandName == LidGuardPipeCommands.Stop,
            SessionEndReason = commandName == LidGuardPipeCommands.Stop ? CreateSessionEndReason(hookInput) : string.Empty,
            WatchedProcessIdentifier = watchedProcessIdentifier,
            InputPrompt = commandName == LidGuardPipeCommands.Start ? hookInput.Prompt : string.Empty,
            WorkingDirectory = GetWorkingDirectory(hookInput),
            TranscriptPath = hookInput.TranscriptPath,
            HasSettings = hasSettings,
            Settings = settings
        };

        var startRuntimeIfUnavailable = commandName == LidGuardPipeCommands.Start;
        var runtimeClientDiagnostics = new LidGuardRuntimeClientDiagnostics();
        var ipcStopwatch = Stopwatch.StartNew();
        var response = await new LidGuardRuntimeClient().SendAsync(request, startRuntimeIfUnavailable, diagnostics: runtimeClientDiagnostics);
        ipcStopwatch.Stop();
        timing.InterprocessCommunicationDuration = ipcStopwatch.Elapsed;
        CodexHookEventLog.AppendRuntimeResult(
            hookInput,
            commandName,
            response.Succeeded,
            response.RuntimeUnavailable,
            response.ActiveSessionCount,
            response.Message,
            timing.CreateRuntimeResultDetails(runtimeClientDiagnostics));
        return 0;
    }

    private static string GetSessionIdentifier(CodexHookInput hookInput)
    {
        if (!string.IsNullOrWhiteSpace(hookInput.SessionIdentifier)) return hookInput.SessionIdentifier;

        var workingDirectory = GetWorkingDirectory(hookInput);
        var normalizedWorkingDirectory = NormalizeWorkingDirectory(workingDirectory);
        return $"{AgentProvider.Codex}:{normalizedWorkingDirectory}";
    }

    private static string GetWorkingDirectory(CodexHookInput hookInput) => string.IsNullOrWhiteSpace(hookInput.WorkingDirectory) ? Environment.CurrentDirectory : hookInput.WorkingDirectory;

    private static string CreateSessionEndReason(CodexHookInput hookInput)
        => string.IsNullOrWhiteSpace(hookInput.HookEventName) ? "codex-hook-stop" : hookInput.HookEventName;

    private static string NormalizeWorkingDirectory(string workingDirectory)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(workingDirectory)); }
        catch { return workingDirectory; }
    }

    private static void WriteConfigTomlSnippet(string hookCommand) => Console.WriteLine(CodexHookConfigTomlDocument.InstallManagedHookBlock(string.Empty, hookCommand).TrimEnd());

    private static void WriteHooksJsonSnippet(string hookCommand)
    {
        var jsonCommandLiteral = CodexHookConfigTomlDocument.ToJsonStringLiteral(hookCommand);
        var hookBlockDefinitions = new (string HookEventName, string StatusMessage)[]
        {
            (CodexHookEventNames.UserPromptSubmit, LidGuardText.HookStatusMessageStartingTurnProtection),
            (CodexHookEventNames.PermissionRequest, LidGuardText.HookStatusMessageRespondingToClosedLidPermissionRequest),
            (CodexHookEventNames.Stop, LidGuardText.HookStatusMessageStoppingSessionProtection)
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

