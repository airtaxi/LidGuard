using System.Text.Json;
using LidGuard.Ipc;
using LidGuard.Settings;
using LidGuardLib.Commons.Hooks;
using LidGuardLib.Commons.Sessions;
using LidGuardLib.Commons.Settings;
using LidGuardLib.Windows.Hooks;

namespace LidGuard.Hooks;

internal static class ClaudeHookCommand
{
    private const string HooksJsonFormat = "hooks-json";
    private const string SettingsJsonFormat = "settings-json";

    public static async Task<int> RunAsync()
    {
        var hookInputJson = await Console.In.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(hookInputJson))
        {
            WindowsClaudeHookEventLog.AppendMessage("LidGuard Claude hook received empty input.");
            return 0;
        }

        ClaudeHookInput hookInput;
        try
        {
            hookInput = JsonSerializer.Deserialize(hookInputJson, LidGuardJsonSerializerContext.Default.ClaudeHookInput);
        }
        catch (JsonException exception)
        {
            WindowsClaudeHookEventLog.AppendMessage($"LidGuard Claude hook could not parse input: {exception.Message}");
            return 0;
        }

        if (hookInput is null)
        {
            WindowsClaudeHookEventLog.AppendMessage("LidGuard Claude hook could not parse input.");
            return 0;
        }

        WindowsClaudeHookEventLog.AppendReceived(hookInput);
        var hookEventName = hookInput.HookEventName.Trim();
        if (hookEventName.Equals(ClaudeHookEventNames.UserPromptSubmit, StringComparison.Ordinal)) return await SendRuntimeRequestAsync(LidGuardPipeCommands.Start, hookInput);
        if (hookEventName.Equals(ClaudeHookEventNames.PermissionRequest, StringComparison.Ordinal)) return WritePermissionRequestDecision();
        if (ClaudeHookEventNames.IsStopTrigger(hookEventName)) return await SendRuntimeRequestAsync(LidGuardPipeCommands.Stop, hookInput);

        return 0;
    }

    public static int WriteHookSnippet(IReadOnlyDictionary<string, string> options)
    {
        var format = GetOption(options, "format");
        if (string.IsNullOrWhiteSpace(format)) format = SettingsJsonFormat;

        var executablePath = GetOption(options, "executable", "exe", "path");
        if (string.IsNullOrWhiteSpace(executablePath)) executablePath = WindowsHookCommandUtilities.GetDefaultHookExecutableReference();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            Console.Error.WriteLine("An executable path or command name is required. Use --executable <path>.");
            return 1;
        }

        var hookCommand = WindowsHookCommandUtilities.CreateHookCommand(executablePath, LidGuardPipeCommands.ClaudeHook);

        if (format.Equals(SettingsJsonFormat, StringComparison.OrdinalIgnoreCase) || format.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(ClaudeHookSettingsJsonDocument.CreateSettingsJsonSnippet(hookCommand));
            return 0;
        }

        if (format.Equals(HooksJsonFormat, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(ClaudeHookSettingsJsonDocument.CreateHooksJsonSnippet(hookCommand));
            return 0;
        }

        Console.Error.WriteLine("Unsupported Claude hook snippet format. Use settings-json or hooks-json.");
        return 1;
    }

    private static int WritePermissionRequestDecision()
    {
        if (!LidGuardSettingsStore.TryLoadOrCreate(out var settings, out var settingsMessage))
        {
            WindowsClaudeHookEventLog.AppendMessage(settingsMessage);
            settings = LidGuardSettings.HeadlessRuntimeDefault;
        }

        WindowsClaudeHookEventLog.AppendMessage($"LidGuard Claude hook handled PermissionRequest with {settings.PermissionRequestBehavior}.");
        return HookPermissionRequestDecisionOutput.Write(settings);
    }

    private static async Task<int> SendRuntimeRequestAsync(string commandName, ClaudeHookInput hookInput)
    {
        // Claude Code hook handling accepts exit 0 + empty stdout as a no-op success,
        // while structured JSON is only needed when a hook intentionally makes a decision.
        var hasSettings = false;
        var settings = LidGuardSettings.Default;
        if (commandName == LidGuardPipeCommands.Start)
        {
            if (!LidGuardSettingsStore.TryLoadOrCreate(out settings, out var settingsMessage))
            {
                WindowsClaudeHookEventLog.AppendMessage(settingsMessage);
                return 0;
            }

            hasSettings = true;
        }

        var request = new LidGuardPipeRequest
        {
            Command = commandName,
            Provider = AgentProvider.Claude,
            SessionIdentifier = GetSessionIdentifier(hookInput),
            WorkingDirectory = GetWorkingDirectory(hookInput),
            HasSettings = hasSettings,
            Settings = settings
        };

        var startRuntimeIfUnavailable = commandName == LidGuardPipeCommands.Start;
        var response = await new LidGuardRuntimeClient().SendAsync(request, startRuntimeIfUnavailable);
        WindowsClaudeHookEventLog.AppendRuntimeResult(hookInput, commandName, response.Succeeded, response.RuntimeUnavailable, response.ActiveSessionCount, response.Message);
        return 0;
    }

    private static string GetSessionIdentifier(ClaudeHookInput hookInput)
    {
        if (!string.IsNullOrWhiteSpace(hookInput.SessionIdentifier)) return hookInput.SessionIdentifier;

        var workingDirectory = GetWorkingDirectory(hookInput);
        var normalizedWorkingDirectory = NormalizeWorkingDirectory(workingDirectory);
        return $"{AgentProvider.Claude}:{normalizedWorkingDirectory}";
    }

    private static string GetWorkingDirectory(ClaudeHookInput hookInput) => string.IsNullOrWhiteSpace(hookInput.WorkingDirectory) ? Environment.CurrentDirectory : hookInput.WorkingDirectory;

    private static string NormalizeWorkingDirectory(string workingDirectory)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(workingDirectory)); }
        catch { return workingDirectory; }
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
