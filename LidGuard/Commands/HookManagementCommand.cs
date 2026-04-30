using LidGuardLib.Commons.Hooks;
using LidGuardLib.Commons.Sessions;
using LidGuardLib.Hooks;

namespace LidGuard.Commands;

internal static class HookManagementCommand
{
    public static int WriteHookStatus(IReadOnlyDictionary<string, string> options)
    {
        if (!TrySelectHookProviders(options, "Show hook status for provider", true, out var selectedProviders, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        ManagedProviderSelection.ResolveAvailableProviders(
            selectedProviders,
            ManagedProviderConfigurationRoots.GetHookCandidatePaths,
            out var providers,
            out var skippedProviderMessages);

        ManagedProviderSelection.WriteSkippedProviderMessages(skippedProviderMessages);
        if (providers.Count == 0) return ManagedProviderSelection.WriteNoAvailableProvidersFound();

        var exitCode = 0;
        foreach (var provider in providers)
        {
            if (providers.Count > 1) Console.WriteLine($"{provider} hook status:");
            var providerExitCode = provider switch
            {
                AgentProvider.Codex => WriteCodexHookStatus(options),
                AgentProvider.Claude => WriteClaudeHookStatus(options),
                AgentProvider.GitHubCopilot => WriteGitHubCopilotHookStatus(options),
                _ => WriteUnsupportedProvider()
            };

            if (providerExitCode != 0) exitCode = providerExitCode;
            if (providers.Count > 1) Console.WriteLine();
        }

        return exitCode;
    }

    public static int InstallHook(IReadOnlyDictionary<string, string> options)
    {
        if (!TrySelectHookProviders(options, "Install hooks for provider", true, out var selectedProviders, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        ManagedProviderSelection.ResolveAvailableProviders(
            selectedProviders,
            ManagedProviderConfigurationRoots.GetHookCandidatePaths,
            out var providers,
            out var skippedProviderMessages);

        ManagedProviderSelection.WriteSkippedProviderMessages(skippedProviderMessages);
        if (providers.Count == 0) return ManagedProviderSelection.WriteNoAvailableProvidersFound();

        var exitCode = 0;
        foreach (var provider in providers)
        {
            if (providers.Count > 1) Console.WriteLine($"Installing {provider} hook...");
            var providerExitCode = provider switch
            {
                AgentProvider.Codex => InstallCodexHook(options),
                AgentProvider.Claude => InstallClaudeHook(options),
                AgentProvider.GitHubCopilot => InstallGitHubCopilotHook(options),
                _ => WriteUnsupportedProvider()
            };

            if (providerExitCode != 0) exitCode = providerExitCode;
        }

        return exitCode;
    }

    public static int RemoveHook(IReadOnlyDictionary<string, string> options)
    {
        if (!TrySelectHookProviders(options, "Remove hooks for provider", true, out var selectedProviders, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        ManagedProviderSelection.ResolveAvailableProviders(
            selectedProviders,
            ManagedProviderConfigurationRoots.GetHookCandidatePaths,
            out var providers,
            out var skippedProviderMessages);

        ManagedProviderSelection.WriteSkippedProviderMessages(skippedProviderMessages);
        if (providers.Count == 0) return ManagedProviderSelection.WriteNoAvailableProvidersFound();

        var exitCode = 0;
        foreach (var provider in providers)
        {
            if (providers.Count > 1) Console.WriteLine($"Removing {provider} hook...");
            var providerExitCode = provider switch
            {
                AgentProvider.Codex => RemoveCodexHook(options),
                AgentProvider.Claude => RemoveClaudeHook(options),
                AgentProvider.GitHubCopilot => RemoveGitHubCopilotHook(options),
                _ => WriteUnsupportedProvider()
            };

            if (providerExitCode != 0) exitCode = providerExitCode;
        }

        return exitCode;
    }

    public static int WriteHookEvents(IReadOnlyDictionary<string, string> options)
    {
        if (!TrySelectHookProviders(options, "Show hook events for provider", false, out var selectedProviders, out var providerMessage))
        {
            Console.Error.WriteLine(providerMessage);
            return 1;
        }

        if (!TryParseMaximumLineCount(options, out var maximumLineCount, out var lineCountMessage))
        {
            Console.Error.WriteLine(lineCountMessage);
            return 1;
        }

        ManagedProviderSelection.ResolveAvailableProviders(
            selectedProviders,
            ManagedProviderConfigurationRoots.GetHookCandidatePaths,
            out var providers,
            out var skippedProviderMessages);

        ManagedProviderSelection.WriteSkippedProviderMessages(skippedProviderMessages);
        if (providers.Count == 0) return ManagedProviderSelection.WriteNoAvailableProvidersFound();

        var exitCode = 0;
        foreach (var provider in providers)
        {
            var eventLines = provider switch
            {
                AgentProvider.Codex => CodexHookEventLog.ReadRecentLines(maximumLineCount),
                AgentProvider.Claude => ClaudeHookEventLog.ReadRecentLines(maximumLineCount),
                AgentProvider.GitHubCopilot => GitHubCopilotHookEventLog.ReadRecentLines(maximumLineCount),
                _ => null
            };

            if (eventLines is null)
            {
                Console.Error.WriteLine("Only Codex, Claude, and GitHub Copilot hook event logs are implemented.");
                exitCode = 1;
                continue;
            }

            if (providers.Count > 1) Console.WriteLine($"{provider} hook events:");
            if (eventLines.Count == 0)
            {
                Console.WriteLine("<empty>");
            }
            else
            {
                foreach (var eventLine in eventLines) Console.WriteLine(eventLine);
            }

            if (providers.Count > 1) Console.WriteLine();
        }

        return exitCode;
    }

    private static int WriteCodexHookStatus(IReadOnlyDictionary<string, string> options)
    {
        var installer = new CodexHookInstaller();
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
        var installer = new ClaudeHookInstaller();
        if (!TryCreateClaudeHookInstallationRequest(options, installer, out var request, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var inspection = installer.Inspect(request);
        WriteClaudeHookInspection(inspection);
        return 0;
    }

    private static int WriteGitHubCopilotHookStatus(IReadOnlyDictionary<string, string> options)
    {
        var installer = new GitHubCopilotHookInstaller();
        if (!TryCreateGitHubCopilotHookInstallationRequest(options, installer, out var request, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var inspection = installer.Inspect(request);
        WriteGitHubCopilotHookInspection(inspection);
        return 0;
    }

    private static int InstallCodexHook(IReadOnlyDictionary<string, string> options)
    {
        var installer = new CodexHookInstaller();
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

    private static int InstallGitHubCopilotHook(IReadOnlyDictionary<string, string> options)
    {
        var installer = new GitHubCopilotHookInstaller();
        if (!TryCreateGitHubCopilotHookInstallationRequest(options, installer, out var request, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var result = installer.Install(request);
        WriteGitHubCopilotHookInspection(result.Inspection);

        if (!string.IsNullOrWhiteSpace(result.BackupFilePath)) Console.WriteLine($"Backup: {result.BackupFilePath}");
        Console.WriteLine($"Changed: {result.Changed}");
        Console.WriteLine($"Message: {result.Message}");
        return result.Succeeded ? 0 : 1;
    }

    private static int RemoveCodexHook(IReadOnlyDictionary<string, string> options)
    {
        var installer = new CodexHookInstaller();
        if (!TryCreateCodexHookInstallationRequest(options, installer, out var request, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var result = installer.Remove(request);
        WriteCodexHookInspection(result.Inspection);

        if (!string.IsNullOrWhiteSpace(result.BackupFilePath)) Console.WriteLine($"Backup: {result.BackupFilePath}");
        Console.WriteLine($"Changed: {result.Changed}");
        Console.WriteLine($"Message: {result.Message}");
        return result.Succeeded ? 0 : 1;
    }

    private static int RemoveGitHubCopilotHook(IReadOnlyDictionary<string, string> options)
    {
        var installer = new GitHubCopilotHookInstaller();
        if (!TryCreateGitHubCopilotHookInstallationRequest(options, installer, out var request, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var result = installer.Remove(request);
        WriteGitHubCopilotHookInspection(result.Inspection);

        if (!string.IsNullOrWhiteSpace(result.BackupFilePath)) Console.WriteLine($"Backup: {result.BackupFilePath}");
        Console.WriteLine($"Changed: {result.Changed}");
        Console.WriteLine($"Message: {result.Message}");
        return result.Succeeded ? 0 : 1;
    }

    private static int InstallClaudeHook(IReadOnlyDictionary<string, string> options)
    {
        var installer = new ClaudeHookInstaller();
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

    private static int RemoveClaudeHook(IReadOnlyDictionary<string, string> options)
    {
        var installer = new ClaudeHookInstaller();
        if (!TryCreateClaudeHookInstallationRequest(options, installer, out var request, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var result = installer.Remove(request);
        WriteClaudeHookInspection(result.Inspection);

        if (!string.IsNullOrWhiteSpace(result.BackupFilePath)) Console.WriteLine($"Backup: {result.BackupFilePath}");
        Console.WriteLine($"Changed: {result.Changed}");
        Console.WriteLine($"Message: {result.Message}");
        return result.Succeeded ? 0 : 1;
    }

    private static bool TryCreateCodexHookInstallationRequest(
        IReadOnlyDictionary<string, string> options,
        CodexHookInstaller installer,
        out CodexHookInstallationRequest request,
        out string message)
    {
        request = null;
        message = string.Empty;
        var configurationFilePath = CommandOptionReader.GetOption(options, "config", "configuration", "configuration-file");
        request = installer.CreateDefaultRequest(configurationFilePath);
        return true;
    }

    private static bool TryCreateGitHubCopilotHookInstallationRequest(
        IReadOnlyDictionary<string, string> options,
        GitHubCopilotHookInstaller installer,
        out GitHubCopilotHookInstallationRequest request,
        out string message)
    {
        request = null;
        message = string.Empty;

        var configurationFilePath = CommandOptionReader.GetOption(options, "config", "configuration", "configuration-file");
        request = installer.CreateDefaultRequest(configurationFilePath);
        return true;
    }

    private static bool TryCreateClaudeHookInstallationRequest(
        IReadOnlyDictionary<string, string> options,
        ClaudeHookInstaller installer,
        out ClaudeHookInstallationRequest request,
        out string message)
    {
        request = null;
        message = string.Empty;

        var configurationFilePath = CommandOptionReader.GetOption(options, "config", "configuration", "configuration-file");
        request = installer.CreateDefaultRequest(configurationFilePath);
        return true;
    }

    private static bool TrySelectHookProviders(
        IReadOnlyDictionary<string, string> options,
        string prompt,
        bool rejectSharedConfigurationFile,
        out IReadOnlyList<AgentProvider> providers,
        out string message)
    {
        providers = [];
        message = string.Empty;

        if (!ManagedProviderSelection.TrySelectProviders(options, prompt, out providers, out message)) return false;
        if (!rejectSharedConfigurationFile || providers.Count < 2 || string.IsNullOrWhiteSpace(CommandOptionReader.GetOption(options, "config", "configuration", "configuration-file"))) return true;

        message = "The config option cannot be used with all providers because each provider has a different configuration file.";
        return false;
    }

    private static bool TryParseMaximumLineCount(IReadOnlyDictionary<string, string> options, out int maximumLineCount, out string message)
    {
        maximumLineCount = 50;
        message = string.Empty;

        var countText = CommandOptionReader.GetOption(options, "count", "lines", "take");
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
        Console.WriteLine($"  Required stop hooks: {inspection.HasRequiredStopHooks}");
        Console.WriteLine($"  Optional SessionEnd hook: {inspection.HasSessionEndHook}");
        Console.WriteLine($"  Valid command: {inspection.HasValidHookCommand}");
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
        Console.WriteLine($"  PreToolUse hook: {inspection.HasPreToolUseHook}");
        Console.WriteLine($"  PostToolUse hook: {inspection.HasPostToolUseHook}");
        Console.WriteLine($"  PostToolUseFailure hook: {inspection.HasPostToolUseFailureHook}");
        Console.WriteLine($"  Stop hook: {inspection.HasStopHook}");
        Console.WriteLine($"  StopFailure hook: {inspection.HasStopFailureHook}");
        Console.WriteLine($"  Elicitation hook: {inspection.HasElicitationHook}");
        Console.WriteLine($"  PermissionRequest hook: {inspection.HasPermissionRequestHook}");
        Console.WriteLine($"  Notification hook: {inspection.HasNotificationHook}");
        Console.WriteLine($"  SessionEnd hook: {inspection.HasSessionEndHook}");
        Console.WriteLine($"  All stop hooks: {inspection.HasAllStopHooks}");
        Console.WriteLine($"  Expected command: {inspection.HasExpectedHookCommand}");
        Console.WriteLine($"  Expected notification matcher: {inspection.HasExpectedNotificationMatcher}");
        Console.WriteLine($"  Expected shell: {inspection.HasExpectedHookShell}");
        Console.WriteLine($"  Message: {inspection.Message}");
    }

    private static void WriteGitHubCopilotHookInspection(GitHubCopilotHookInstallationInspection inspection)
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
        Console.WriteLine($"  SessionStart hook: {inspection.HasSessionStartHook}");
        Console.WriteLine($"  SessionEnd hook: {inspection.HasSessionEndHook}");
        Console.WriteLine($"  UserPromptSubmitted hook: {inspection.HasUserPromptSubmittedHook}");
        Console.WriteLine($"  PreToolUse hook: {inspection.HasPreToolUseHook}");
        Console.WriteLine($"  PostToolUse hook: {inspection.HasPostToolUseHook}");
        Console.WriteLine($"  PermissionRequest hook: {inspection.HasPermissionRequestHook}");
        Console.WriteLine($"  AgentStop hook: {inspection.HasAgentStopHook}");
        Console.WriteLine($"  ErrorOccurred hook: {inspection.HasErrorOccurredHook}");
        Console.WriteLine($"  Notification hook: {inspection.HasNotificationHook}");
        Console.WriteLine($"  Expected commands: {inspection.HasExpectedHookCommands}");
        Console.WriteLine($"  Expected notification matcher: {inspection.HasExpectedNotificationMatcher}");
        Console.WriteLine($"  Conflicting agentStop hooks: {inspection.HasConflictingAgentStopHooks}");
        Console.WriteLine($"  Conflict sources: {(inspection.ConflictingAgentStopHookSources.Count == 0 ? "<none>" : string.Join(" | ", inspection.ConflictingAgentStopHookSources))}");
        Console.WriteLine($"  Message: {inspection.Message}");
    }

    private static string GetHookLogFilePath(AgentProvider provider)
    {
        if (provider == AgentProvider.Codex) return CodexHookEventLog.GetDefaultLogFilePath();
        if (provider == AgentProvider.Claude) return ClaudeHookEventLog.GetDefaultLogFilePath();
        if (provider == AgentProvider.GitHubCopilot) return GitHubCopilotHookEventLog.GetDefaultLogFilePath();
        return string.Empty;
    }

    private static int WriteUnsupportedProvider()
    {
        Console.Error.WriteLine("Only Codex, Claude, and GitHub Copilot hook management are implemented.");
        return 1;
    }

}

