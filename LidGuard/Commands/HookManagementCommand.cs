using LidGuardLib.Commons.Hooks;
using LidGuardLib.Commons.Sessions;
using LidGuardLib.Windows.Hooks;

namespace LidGuard.Commands;

internal static class HookManagementCommand
{
    public static int WriteHookStatus(IReadOnlyDictionary<string, string> options)
    {
        if (!TryParseProvider(options, out var provider, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        return provider switch
        {
            AgentProvider.Codex => WriteCodexHookStatus(options),
            AgentProvider.Claude => WriteClaudeHookStatus(options),
            _ => WriteUnsupportedProvider()
        };
    }

    public static int InstallHook(IReadOnlyDictionary<string, string> options)
    {
        if (!TryParseProvider(options, out var provider, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        return provider switch
        {
            AgentProvider.Codex => InstallCodexHook(options),
            AgentProvider.Claude => InstallClaudeHook(options),
            _ => WriteUnsupportedProvider()
        };
    }

    public static int WriteHookEvents(IReadOnlyDictionary<string, string> options)
    {
        if (!TryParseProvider(options, out var provider, out var providerMessage))
        {
            Console.Error.WriteLine(providerMessage);
            return 1;
        }

        if (!TryParseMaximumLineCount(options, out var maximumLineCount, out var lineCountMessage))
        {
            Console.Error.WriteLine(lineCountMessage);
            return 1;
        }

        var eventLines = provider switch
        {
            AgentProvider.Codex => WindowsCodexHookEventLog.ReadRecentLines(maximumLineCount),
            AgentProvider.Claude => WindowsClaudeHookEventLog.ReadRecentLines(maximumLineCount),
            _ => null
        };

        if (eventLines is null)
        {
            Console.Error.WriteLine("Only Codex and Claude hook event logs are implemented.");
            return 1;
        }

        if (eventLines.Count == 0)
        {
            Console.WriteLine("<empty>");
            return 0;
        }

        foreach (var eventLine in eventLines) Console.WriteLine(eventLine);
        return 0;
    }

    private static int WriteCodexHookStatus(IReadOnlyDictionary<string, string> options)
    {
        var installer = new WindowsCodexHookInstaller();
        if (!TryCreateCodexHookInstallationRequest(options, installer, out var request, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var inspection = installer.Inspect(request);
        WriteCodexHookInspection(inspection);
        return 0;
    }

    private static int WriteClaudeHookStatus(IReadOnlyDictionary<string, string> options)
    {
        var installer = new WindowsClaudeHookInstaller();
        if (!TryCreateClaudeHookInstallationRequest(options, installer, out var request, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var inspection = installer.Inspect(request);
        WriteClaudeHookInspection(inspection);
        return 0;
    }

    private static int InstallCodexHook(IReadOnlyDictionary<string, string> options)
    {
        var installer = new WindowsCodexHookInstaller();
        if (!TryCreateCodexHookInstallationRequest(options, installer, out var request, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var result = installer.Install(request);
        WriteCodexHookInspection(result.Inspection);

        if (!string.IsNullOrWhiteSpace(result.BackupFilePath)) Console.WriteLine($"Backup: {result.BackupFilePath}");
        Console.WriteLine($"Changed: {result.Changed}");
        Console.WriteLine($"Message: {result.Message}");
        return result.Succeeded ? 0 : 1;
    }

    private static int InstallClaudeHook(IReadOnlyDictionary<string, string> options)
    {
        var installer = new WindowsClaudeHookInstaller();
        if (!TryCreateClaudeHookInstallationRequest(options, installer, out var request, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var result = installer.Install(request);
        WriteClaudeHookInspection(result.Inspection);

        if (!string.IsNullOrWhiteSpace(result.BackupFilePath)) Console.WriteLine($"Backup: {result.BackupFilePath}");
        Console.WriteLine($"Changed: {result.Changed}");
        Console.WriteLine($"Message: {result.Message}");
        return result.Succeeded ? 0 : 1;
    }

    private static bool TryCreateCodexHookInstallationRequest(
        IReadOnlyDictionary<string, string> options,
        WindowsCodexHookInstaller installer,
        out CodexHookInstallationRequest request,
        out string message)
    {
        request = null;
        message = string.Empty;
        var configurationFilePath = GetOption(options, "config", "configuration", "configuration-file");
        var hookExecutablePath = GetOption(options, "executable", "hook-executable", "path");
        request = installer.CreateDefaultRequest(hookExecutablePath, configurationFilePath);
        return true;
    }

    private static bool TryCreateClaudeHookInstallationRequest(
        IReadOnlyDictionary<string, string> options,
        WindowsClaudeHookInstaller installer,
        out ClaudeHookInstallationRequest request,
        out string message)
    {
        request = null;
        message = string.Empty;

        var configurationFilePath = GetOption(options, "config", "configuration", "configuration-file");
        var hookExecutablePath = GetOption(options, "executable", "hook-executable", "path");
        request = installer.CreateDefaultRequest(hookExecutablePath, configurationFilePath);
        return true;
    }

    private static bool TryParseProvider(IReadOnlyDictionary<string, string> options, out AgentProvider provider, out string message)
    {
        provider = AgentProvider.Codex;
        message = string.Empty;

        var providerText = GetOption(options, "provider");
        if (string.IsNullOrWhiteSpace(providerText)) return true;

        provider = providerText.Trim().ToLowerInvariant() switch
        {
            "codex" => AgentProvider.Codex,
            "claude" => AgentProvider.Claude,
            "copilot" or "github-copilot" or "githubcopilot" => AgentProvider.GitHubCopilot,
            "custom" => AgentProvider.Custom,
            "unknown" => AgentProvider.Unknown,
            _ => AgentProvider.Unknown
        };

        if (provider != AgentProvider.Unknown || providerText.Equals("unknown", StringComparison.OrdinalIgnoreCase)) return true;

        message = "Unsupported provider. Use codex or claude.";
        return false;
    }

    private static bool TryParseMaximumLineCount(IReadOnlyDictionary<string, string> options, out int maximumLineCount, out string message)
    {
        maximumLineCount = 50;
        message = string.Empty;

        var countText = GetOption(options, "count", "lines", "take");
        if (string.IsNullOrWhiteSpace(countText)) return true;
        if (int.TryParse(countText, out maximumLineCount) && maximumLineCount > 0) return true;

        message = "The hook event count must be a positive integer.";
        return false;
    }

    private static void WriteCodexHookInspection(CodexHookInstallationInspection inspection)
    {
        Console.WriteLine("Hook installation:");
        Console.WriteLine($"  Provider: {inspection.Provider}");
        Console.WriteLine($"  Status: {inspection.Status}");
        Console.WriteLine($"  Installed: {inspection.IsInstalled}");
        Console.WriteLine($"  Config: {inspection.ConfigurationFilePath}");
        Console.WriteLine($"  Config exists: {inspection.ConfigurationFileExists}");
        Console.WriteLine($"  Executable: {inspection.HookExecutablePath}");
        Console.WriteLine($"  Command: {inspection.HookCommand}");
        Console.WriteLine($"  Hook log: {GetHookLogFilePath(inspection.Provider)}");
        Console.WriteLine($"  Feature flag: {inspection.HasCodexHooksFeatureFlag}");
        Console.WriteLine($"  Managed block: {inspection.HasManagedBlock}");
        Console.WriteLine($"  UserPromptSubmit hook: {inspection.HasUserPromptSubmitHook}");
        Console.WriteLine($"  Stop hook: {inspection.HasStopHook}");
        Console.WriteLine($"  PermissionRequest hook: {inspection.HasPermissionRequestHook}");
        Console.WriteLine($"  SessionEnd hook: {inspection.HasSessionEndHook}");
        Console.WriteLine($"  All stop hooks: {inspection.HasAllStopHooks}");
        Console.WriteLine($"  Expected command: {inspection.HasExpectedHookCommand}");
        Console.WriteLine($"  Message: {inspection.Message}");
    }

    private static void WriteClaudeHookInspection(ClaudeHookInstallationInspection inspection)
    {
        Console.WriteLine("Hook installation:");
        Console.WriteLine($"  Provider: {inspection.Provider}");
        Console.WriteLine($"  Status: {inspection.Status}");
        Console.WriteLine($"  Installed: {inspection.IsInstalled}");
        Console.WriteLine($"  Config: {inspection.ConfigurationFilePath}");
        Console.WriteLine($"  Config exists: {inspection.ConfigurationFileExists}");
        Console.WriteLine($"  Executable: {inspection.HookExecutablePath}");
        Console.WriteLine($"  Command: {inspection.HookCommand}");
        Console.WriteLine($"  Hook log: {GetHookLogFilePath(inspection.Provider)}");
        Console.WriteLine($"  Hooks object: {inspection.HasHooksObject}");
        Console.WriteLine($"  Managed hooks: {inspection.HasManagedHookEntries}");
        Console.WriteLine($"  UserPromptSubmit hook: {inspection.HasUserPromptSubmitHook}");
        Console.WriteLine($"  Stop hook: {inspection.HasStopHook}");
        Console.WriteLine($"  StopFailure hook: {inspection.HasStopFailureHook}");
        Console.WriteLine($"  PermissionRequest hook: {inspection.HasPermissionRequestHook}");
        Console.WriteLine($"  SessionEnd hook: {inspection.HasSessionEndHook}");
        Console.WriteLine($"  All stop hooks: {inspection.HasAllStopHooks}");
        Console.WriteLine($"  Expected command: {inspection.HasExpectedHookCommand}");
        Console.WriteLine($"  Expected shell: {inspection.HasExpectedHookShell}");
        Console.WriteLine($"  Message: {inspection.Message}");
    }

    private static string GetHookLogFilePath(AgentProvider provider)
    {
        if (provider == AgentProvider.Codex) return WindowsCodexHookEventLog.GetDefaultLogFilePath();
        if (provider == AgentProvider.Claude) return WindowsClaudeHookEventLog.GetDefaultLogFilePath();
        return string.Empty;
    }

    private static int WriteUnsupportedProvider()
    {
        Console.Error.WriteLine("Only Codex and Claude hook management are implemented.");
        return 1;
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

