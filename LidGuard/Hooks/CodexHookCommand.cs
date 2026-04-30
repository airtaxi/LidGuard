using System.Text.Json;
using LidGuard.Ipc;
using LidGuard.Settings;
using LidGuardLib.Commons.Hooks;
using LidGuardLib.Commons.Power;
using LidGuardLib.Commons.Sessions;
using LidGuardLib.Commons.Settings;
using LidGuardLib.Windows.Hooks;

namespace LidGuard.Hooks;

internal static class CodexHookCommand
{
    private const string ConfigTomlFormat = "config-toml";
    private const string HooksJsonFormat = "hooks-json";

    public static async Task<int> RunAsync()
    {
        var hookInputJson = await Console.In.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(hookInputJson))
        {
            WindowsCodexHookEventLog.AppendMessage("LidGuard Codex hook received empty input.");
            return 0;
        }

        CodexHookInput hookInput;
        try
        {
            hookInput = JsonSerializer.Deserialize(hookInputJson, LidGuardJsonSerializerContext.Default.CodexHookInput);
        }
        catch (JsonException exception)
        {
            WindowsCodexHookEventLog.AppendMessage($"LidGuard Codex hook could not parse input: {exception.Message}");
            return 0;
        }

        if (hookInput is null)
        {
            WindowsCodexHookEventLog.AppendMessage("LidGuard Codex hook could not parse input.");
            return 0;
        }

        WindowsCodexHookEventLog.AppendReceived(hookInput);
        var hookEventName = hookInput.HookEventName.Trim();
        if (hookEventName.Equals(CodexHookEventNames.UserPromptSubmit, StringComparison.Ordinal)) return await SendRuntimeRequestAsync(LidGuardPipeCommands.Start, hookInput);
        if (hookEventName.Equals(CodexHookEventNames.PermissionRequest, StringComparison.Ordinal)) return await WriteClosedLidPermissionRequestDecisionAsync();
        if (CodexHookEventNames.IsStopTrigger(hookEventName)) return await SendRuntimeRequestAsync(LidGuardPipeCommands.Stop, hookInput);

        return 0;
    }

    public static int WriteHookSnippet(IReadOnlyDictionary<string, string> options)
    {
        var format = GetOption(options, "format");
        if (string.IsNullOrWhiteSpace(format)) format = ConfigTomlFormat;

        var executablePath = WindowsHookCommandUtilities.GetDefaultHookExecutableReference();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            Console.Error.WriteLine("A default LidGuard hook executable or command name could not be resolved.");
            return 1;
        }

        var hookCommand = WindowsHookCommandUtilities.CreateHookCommand(executablePath, LidGuardPipeCommands.CodexHook);

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
            WindowsCodexHookEventLog.AppendMessage($"LidGuard Codex hook skipped PermissionRequest decision because runtime status is unavailable: {response.Message}");
            return 0;
        }

        if (response.LidSwitchState != LidSwitchState.Closed)
        {
            WindowsCodexHookEventLog.AppendMessage($"LidGuard Codex hook left PermissionRequest to Codex because the lid state is {response.LidSwitchState}.");
            return 0;
        }

        WindowsCodexHookEventLog.AppendMessage($"LidGuard Codex hook handled closed-lid PermissionRequest with {response.Settings.ClosedLidPermissionRequestDecision}.");
        return CodexClosedLidPermissionRequestDecisionOutput.Write(response.Settings);
    }

    private static async Task<int> SendRuntimeRequestAsync(string commandName, CodexHookInput hookInput)
    {
        // codex-rs hook handling accepts exit 0 + empty stdout as a no-op success,
        // while non-empty stdout can be interpreted differently per event.
        var hasSettings = false;
        var settings = LidGuardSettings.Default;
        if (commandName == LidGuardPipeCommands.Start)
        {
            if (!LidGuardSettingsStore.TryLoadOrCreate(out settings, out var settingsMessage))
            {
                WindowsCodexHookEventLog.AppendMessage(settingsMessage);
                return 0;
            }

            hasSettings = true;
        }

        var request = new LidGuardPipeRequest
        {
            Command = commandName,
            Provider = AgentProvider.Codex,
            SessionIdentifier = GetSessionIdentifier(hookInput),
            WorkingDirectory = GetWorkingDirectory(hookInput),
            TranscriptPath = hookInput.TranscriptPath,
            HasSettings = hasSettings,
            Settings = settings
        };

        var startRuntimeIfUnavailable = commandName == LidGuardPipeCommands.Start;
        var response = await new LidGuardRuntimeClient().SendAsync(request, startRuntimeIfUnavailable);
        WindowsCodexHookEventLog.AppendRuntimeResult(hookInput, commandName, response.Succeeded, response.RuntimeUnavailable, response.ActiveSessionCount, response.Message);
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
            (CodexHookEventNames.UserPromptSubmit, "Starting LidGuard turn protection"),
            (CodexHookEventNames.PermissionRequest, "Responding to closed-lid permission request"),
            (CodexHookEventNames.Stop, "Stopping LidGuard session protection")
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
        Console.WriteLine($"            \"statusMessage\": \"{statusMessage}\"");
        Console.WriteLine("          }");
        Console.WriteLine("        ]");
        Console.WriteLine("      }");
        Console.WriteLine(hasTrailingComma ? "    ]," : "    ]");
    }

    private static string GetOption(IReadOnlyDictionary<string, string> options, params string[] optionNames)
    {
        foreach (var optionName in optionNames)
        {
            if (options.TryGetValue(optionName, out var optionValue)) return optionValue;
        }

        return string.Empty;
    }
}

