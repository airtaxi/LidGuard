using LidGuard.Hooks;
using LidGuard.Sessions;
using LidGuard.Localization;

namespace LidGuard.Commands;

internal static class HookManagementCommand
{
    public static int WriteHookStatus(IReadOnlyDictionary<string, string> options)
    {
        if (!TrySelectHookProviders(options, LidGuardText.GetResourceString("ManagementPromptHookStatus", "Show hook status for provider"), true, out var selectedProviders, out var message))
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
            if (providers.Count > 1) Console.WriteLine(LidGuardText.ManagementHookStatusTitle(provider));
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
        if (!TrySelectHookProviders(options, LidGuardText.GetResourceString("ManagementPromptHookInstall", "Install hooks for provider"), true, out var selectedProviders, out var message))
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
            if (providers.Count > 1) Console.WriteLine(LidGuardText.ManagementInstallingHook(provider));
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
        if (!TrySelectHookProviders(options, LidGuardText.GetResourceString("ManagementPromptHookRemove", "Remove hooks for provider"), true, out var selectedProviders, out var message))
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
            if (providers.Count > 1) Console.WriteLine(LidGuardText.ManagementRemovingHook(provider));
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
        if (!TrySelectHookProviders(options, LidGuardText.GetResourceString("ManagementPromptHookEvents", "Show hook events for provider"), false, out var selectedProviders, out var providerMessage))
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
                Console.Error.WriteLine(LidGuardText.ManagementUnsupportedHookEventLogs);
                exitCode = 1;
                continue;
            }

            if (providers.Count > 1) Console.WriteLine(LidGuardText.ManagementHookEventsTitle(provider));
            if (eventLines.Count == 0)
            {
                Console.WriteLine(LidGuardText.TextDisplayEmpty);
            }
            else
            {
                foreach (var eventLine in eventLines) Console.WriteLine(LidGuardCommandTimestampFormatter.FormatHookEventLineForDisplay(eventLine));
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

        WriteHookManagementResult(result.BackupFilePath, result.Changed, result.Inspection.Provider, result.Message);
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

        WriteHookManagementResult(result.BackupFilePath, result.Changed, result.Inspection.Provider, result.Message);
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

        WriteHookManagementResult(result.BackupFilePath, result.Changed, result.Inspection.Provider, result.Message);
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

        WriteHookManagementResult(result.BackupFilePath, result.Changed, result.Inspection.Provider, result.Message);
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

        WriteHookManagementResult(result.BackupFilePath, result.Changed, result.Inspection.Provider, result.Message);
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

        WriteHookManagementResult(result.BackupFilePath, result.Changed, result.Inspection.Provider, result.Message);
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

        message = LidGuardText.GetResourceString("ManagementConfigCannotBeUsedWithAllProviders", "The config option cannot be used with all providers because each provider has a different configuration file.");
        return false;
    }

    private static bool TryParseMaximumLineCount(IReadOnlyDictionary<string, string> options, out int maximumLineCount, out string message)
    {
        maximumLineCount = 50;
        message = string.Empty;

        var countText = CommandOptionReader.GetOption(options, "count", "lines", "take");
        if (string.IsNullOrWhiteSpace(countText)) return true;
        if (int.TryParse(countText, out maximumLineCount) && maximumLineCount > 0) return true;

        message = LidGuardText.GetResourceString("ManagementHookEventCountValidation", "The hook event count must be a positive integer.");
        return false;
    }

    private static void WriteHookManagementResult(string backupFilePath, bool changed, AgentProvider provider, string message)
    {
        if (!string.IsNullOrWhiteSpace(backupFilePath)) Console.WriteLine(LidGuardText.ManagementBackup(backupFilePath));
        Console.WriteLine(LidGuardText.ManagementChanged(changed));
        Console.WriteLine(LidGuardText.ManagementMessage(DisplayHookManagementMessage(provider, message)));
    }

    private static void WriteCodexHookInspection(CodexHookInstallationInspection inspection)
    {
        Console.WriteLine(LidGuardText.ManagementHookInstallationTitle);
        WriteField("ManagementLabelProvider", "Provider", inspection.Provider);
        WriteField("ManagementLabelStatus", "Status", inspection.Status);
        WriteField("ManagementLabelInstalled", "Installed", inspection.IsInstalled);
        WriteField("ManagementLabelConfig", "Config", inspection.ConfigurationFilePath);
        WriteField("ManagementLabelConfigExists", "Config exists", inspection.ConfigurationFileExists);
        WriteField("ManagementLabelExecutable", "Executable", inspection.HookExecutablePath);
        WriteField("ManagementLabelCommand", "Command", inspection.HookCommand);
        WriteField("ManagementLabelHookLog", "Hook log", GetHookLogFilePath(inspection.Provider));
        WriteField("ManagementLabelFeatureFlag", "Feature flag", inspection.HasCodexHooksFeatureFlag);
        WriteField("ManagementLabelManagedBlock", "Managed block", inspection.HasManagedBlock);
        WriteField("ManagementLabelUserPromptSubmitHook", "UserPromptSubmit hook", inspection.HasUserPromptSubmitHook);
        WriteField("ManagementLabelStopHook", "Stop hook", inspection.HasStopHook);
        WriteField("ManagementLabelPermissionRequestHook", "PermissionRequest hook", inspection.HasPermissionRequestHook);
        WriteField("ManagementLabelRequiredStopHooks", "Required stop hooks", inspection.HasRequiredStopHooks);
        WriteField("ManagementLabelOptionalSessionEndHook", "Optional SessionEnd hook", inspection.HasSessionEndHook);
        WriteField("ManagementLabelValidCommand", "Valid command", inspection.HasValidHookCommand);
        WriteField("ManagementLabelExpectedCommand", "Expected command", inspection.HasExpectedHookCommand);
        WriteField("ManagementLabelMessage", "Message", DisplayHookManagementMessage(inspection.Provider, inspection.Message));
    }

    private static void WriteClaudeHookInspection(ClaudeHookInstallationInspection inspection)
    {
        Console.WriteLine(LidGuardText.ManagementHookInstallationTitle);
        WriteField("ManagementLabelProvider", "Provider", inspection.Provider);
        WriteField("ManagementLabelStatus", "Status", inspection.Status);
        WriteField("ManagementLabelInstalled", "Installed", inspection.IsInstalled);
        WriteField("ManagementLabelConfig", "Config", inspection.ConfigurationFilePath);
        WriteField("ManagementLabelConfigExists", "Config exists", inspection.ConfigurationFileExists);
        WriteField("ManagementLabelExecutable", "Executable", inspection.HookExecutablePath);
        WriteField("ManagementLabelCommand", "Command", inspection.HookCommand);
        WriteField("ManagementLabelHookLog", "Hook log", GetHookLogFilePath(inspection.Provider));
        WriteField("ManagementLabelHooksObject", "Hooks object", inspection.HasHooksObject);
        WriteField("ManagementLabelManagedHooks", "Managed hooks", inspection.HasManagedHookEntries);
        WriteField("ManagementLabelUserPromptSubmitHook", "UserPromptSubmit hook", inspection.HasUserPromptSubmitHook);
        WriteField("ManagementLabelPreToolUseHook", "PreToolUse hook", inspection.HasPreToolUseHook);
        WriteField("ManagementLabelPostToolUseHook", "PostToolUse hook", inspection.HasPostToolUseHook);
        WriteField("ManagementLabelPostToolUseFailureHook", "PostToolUseFailure hook", inspection.HasPostToolUseFailureHook);
        WriteField("ManagementLabelSubagentStartHook", "SubagentStart hook", inspection.HasSubagentStartHook);
        WriteField("ManagementLabelSubagentStopHook", "SubagentStop hook", inspection.HasSubagentStopHook);
        WriteField("ManagementLabelTaskCreatedHook", "TaskCreated hook", inspection.HasTaskCreatedHook);
        WriteField("ManagementLabelTaskCompletedHook", "TaskCompleted hook", inspection.HasTaskCompletedHook);
        WriteField("ManagementLabelStopHook", "Stop hook", inspection.HasStopHook);
        WriteField("ManagementLabelStopFailureHook", "StopFailure hook", inspection.HasStopFailureHook);
        WriteField("ManagementLabelElicitationHook", "Elicitation hook", inspection.HasElicitationHook);
        WriteField("ManagementLabelPermissionRequestHook", "PermissionRequest hook", inspection.HasPermissionRequestHook);
        WriteField("ManagementLabelNotificationHook", "Notification hook", inspection.HasNotificationHook);
        WriteField("ManagementLabelSessionEndHook", "SessionEnd hook", inspection.HasSessionEndHook);
        WriteField("ManagementLabelAllStopHooks", "All stop hooks", inspection.HasAllStopHooks);
        WriteField("ManagementLabelExpectedCommand", "Expected command", inspection.HasExpectedHookCommand);
        WriteField("ManagementLabelExpectedNotificationMatcher", "Expected notification matcher", inspection.HasExpectedNotificationMatcher);
        WriteField("ManagementLabelExpectedShell", "Expected shell", inspection.HasExpectedHookShell);
        WriteField("ManagementLabelMessage", "Message", DisplayHookManagementMessage(inspection.Provider, inspection.Message));
    }

    private static void WriteGitHubCopilotHookInspection(GitHubCopilotHookInstallationInspection inspection)
    {
        Console.WriteLine(LidGuardText.ManagementHookInstallationTitle);
        WriteField("ManagementLabelProvider", "Provider", inspection.Provider);
        WriteField("ManagementLabelStatus", "Status", inspection.Status);
        WriteField("ManagementLabelInstalled", "Installed", inspection.IsInstalled);
        WriteField("ManagementLabelConfig", "Config", inspection.ConfigurationFilePath);
        WriteField("ManagementLabelConfigExists", "Config exists", inspection.ConfigurationFileExists);
        WriteField("ManagementLabelExecutable", "Executable", inspection.HookExecutablePath);
        WriteField("ManagementLabelCommand", "Command", inspection.HookCommand);
        WriteField("ManagementLabelHookLog", "Hook log", GetHookLogFilePath(inspection.Provider));
        WriteField("ManagementLabelHooksObject", "Hooks object", inspection.HasHooksObject);
        WriteField("ManagementLabelManagedHooks", "Managed hooks", inspection.HasManagedHookEntries);
        WriteField("ManagementLabelSessionStartHook", "SessionStart hook", inspection.HasSessionStartHook);
        WriteField("ManagementLabelSessionEndHook", "SessionEnd hook", inspection.HasSessionEndHook);
        WriteField("ManagementLabelUserPromptSubmittedHook", "UserPromptSubmitted hook", inspection.HasUserPromptSubmittedHook);
        WriteField("ManagementLabelPreToolUseHook", "PreToolUse hook", inspection.HasPreToolUseHook);
        WriteField("ManagementLabelPostToolUseHook", "PostToolUse hook", inspection.HasPostToolUseHook);
        WriteField("ManagementLabelPermissionRequestHook", "PermissionRequest hook", inspection.HasPermissionRequestHook);
        WriteField("ManagementLabelAgentStopHook", "AgentStop hook", inspection.HasAgentStopHook);
        WriteField("ManagementLabelErrorOccurredHook", "ErrorOccurred hook", inspection.HasErrorOccurredHook);
        WriteField("ManagementLabelNotificationHook", "Notification hook", inspection.HasNotificationHook);
        WriteField("ManagementLabelExpectedCommands", "Expected commands", inspection.HasExpectedHookCommands);
        WriteField("ManagementLabelExpectedNotificationMatcher", "Expected notification matcher", inspection.HasExpectedNotificationMatcher);
        WriteField("ManagementLabelConflictingAgentStopHooks", "Conflicting agentStop hooks", inspection.HasConflictingAgentStopHooks);
        WriteField(
            "ManagementLabelConflictSources",
            "Conflict sources",
            inspection.ConflictingAgentStopHookSources.Count == 0 ? LidGuardText.TextDisplayNone : string.Join(" | ", inspection.ConflictingAgentStopHookSources));
        WriteField("ManagementLabelMessage", "Message", DisplayHookManagementMessage(inspection.Provider, inspection.Message));
    }

    private static string DisplayHookManagementMessage(AgentProvider provider, string message)
    {
        var providerName = ManagedProviderSelection.GetProviderDisplayName(provider);
        if (string.IsNullOrWhiteSpace(message)) return LidGuardText.TextDisplayNone;
        if (message.Equals($"{providerName} hook is installed.", StringComparison.Ordinal)) return LidGuardText.HookManagementIsInstalled(providerName);
        if (message.Equals($"{providerName} hook is installed but needs update.", StringComparison.Ordinal)) return LidGuardText.HookManagementInstalledNeedsUpdate(providerName);
        if (message.Equals($"{providerName} hook is not installed.", StringComparison.Ordinal)) return LidGuardText.HookManagementNotInstalled(providerName);
        if (message.Equals($"{providerName} hook is already installed.", StringComparison.Ordinal)) return LidGuardText.HookManagementAlreadyInstalled(providerName);
        if (message.Equals($"{providerName} hook is already installed outside the LidGuard managed block.", StringComparison.Ordinal)) return LidGuardText.HookManagementAlreadyInstalledOutsideManagedBlock(providerName);
        if (message.Equals($"{providerName} hook installed.", StringComparison.Ordinal)) return LidGuardText.HookManagementInstalled(providerName);
        if (message.Equals($"{providerName} hook configuration was written but still needs attention.", StringComparison.Ordinal)) return LidGuardText.HookManagementWrittenNeedsAttention(providerName);
        if (message.Equals($"No LidGuard-managed {providerName} hook was found.", StringComparison.Ordinal)) return LidGuardText.HookManagementNoManagedHookFound(providerName);
        if (message.Equals($"{providerName} hook removed.", StringComparison.Ordinal)) return LidGuardText.HookManagementRemoved(providerName);
        if (message.Equals($"Only {providerName} hook installation is implemented.", StringComparison.Ordinal)) return LidGuardText.HookManagementUnsupportedInstallation(providerName);
        if (message.Equals($"Only {providerName} hook removal is implemented.", StringComparison.Ordinal)) return LidGuardText.HookManagementUnsupportedRemoval(providerName);
        if (message.Equals($"{providerName} configuration file does not exist.", StringComparison.Ordinal)) return LidGuardText.HookManagementConfigurationFileDoesNotExist(providerName);
        if (message.Equals($"{providerName} settings file does not exist.", StringComparison.Ordinal)) return LidGuardText.HookManagementConfigurationFileDoesNotExist(providerName);
        if (message.Equals($"{providerName} hook configuration file does not exist.", StringComparison.Ordinal)) return LidGuardText.HookManagementConfigurationFileDoesNotExist(providerName);
        const string hookExecutableMissingPrefix = "Hook executable or command does not exist: ";
        if (message.StartsWith(hookExecutableMissingPrefix, StringComparison.Ordinal)) return LidGuardText.HookManagementHookExecutableDoesNotExist(message[hookExecutableMissingPrefix.Length..]);
        return message;
    }

    private static void WriteField(string labelResourceName, string fallbackLabel, object value)
    {
        var displayValue = value switch
        {
            bool booleanValue => LidGuardText.DisplayBoolean(booleanValue),
            CodexHookInstallationStatus status => DisplayHookInstallationStatus(status),
            _ => LidGuardText.DisplayOptionalValue(value?.ToString() ?? string.Empty)
        };
        Console.WriteLine(LidGuardText.ManagementField(LidGuardText.GetResourceString(labelResourceName, fallbackLabel), displayValue));
    }

    private static string DisplayHookInstallationStatus(CodexHookInstallationStatus status)
        => LidGuardText.GetResourceString($"DisplayHookInstallationStatus{status}", status.ToString());

    private static string GetHookLogFilePath(AgentProvider provider)
    {
        if (provider == AgentProvider.Codex) return CodexHookEventLog.GetDefaultLogFilePath();
        if (provider == AgentProvider.Claude) return ClaudeHookEventLog.GetDefaultLogFilePath();
        if (provider == AgentProvider.GitHubCopilot) return GitHubCopilotHookEventLog.GetDefaultLogFilePath();
        return string.Empty;
    }

    private static int WriteUnsupportedProvider()
    {
        Console.Error.WriteLine(LidGuardText.ManagementUnsupportedHookManagement);
        return 1;
    }

}

