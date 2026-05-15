using LidGuard.Hooks;
using LidGuard.Sessions;
using LidGuard.Localization;

namespace LidGuard.Commands;

internal static class HookManagementCommand
{
    public static int WriteHookStatus(IReadOnlyDictionary<string, string> options)
    {
        if (!TrySelectHookProviders(options, LocalizationService.GetString("ManagementPromptHookStatus", "Show hook status for provider"), true, out var selectedProviders, out var message))
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
            if (providers.Count > 1) Console.WriteLine(LocalizationService.GetFormattedString("ManagementHookStatusTitle", provider));
            var providerExitCode = WriteProviderHookStatus(provider, options);

            if (providerExitCode != 0) exitCode = providerExitCode;
            if (providers.Count > 1) Console.WriteLine();
        }

        return exitCode;
    }

    public static int InstallHook(IReadOnlyDictionary<string, string> options)
    {
        if (!TrySelectHookProviders(options, LocalizationService.GetString("ManagementPromptHookInstall", "Install hooks for provider"), true, out var selectedProviders, out var message))
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
            if (providers.Count > 1) Console.WriteLine(LocalizationService.GetFormattedString("ManagementInstallingHook", provider));
            var providerExitCode = InstallProviderHook(provider, options);

            if (providerExitCode != 0) exitCode = providerExitCode;
        }

        return exitCode;
    }

    public static int RemoveHook(IReadOnlyDictionary<string, string> options)
    {
        if (!TrySelectHookProviders(options, LocalizationService.GetString("ManagementPromptHookRemove", "Remove hooks for provider"), true, out var selectedProviders, out var message))
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
            if (providers.Count > 1) Console.WriteLine(LocalizationService.GetFormattedString("ManagementRemovingHook", provider));
            var providerExitCode = RemoveProviderHook(provider, options);

            if (providerExitCode != 0) exitCode = providerExitCode;
        }

        return exitCode;
    }

    public static int WriteHookEvents(IReadOnlyDictionary<string, string> options)
    {
        if (!TrySelectHookProviders(options, LocalizationService.GetString("ManagementPromptHookEvents", "Show hook events for provider"), false, out var selectedProviders, out var providerMessage))
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
                Console.Error.WriteLine(LocalizationService.GetString("ManagementUnsupportedHookEventLogs"));
                exitCode = 1;
                continue;
            }

            if (providers.Count > 1) Console.WriteLine(LocalizationService.GetFormattedString("ManagementHookEventsTitle", provider));
            if (eventLines.Count == 0)
            {
                Console.WriteLine(LocalizationService.GetString("TextDisplayEmpty"));
            }
            else
            {
                foreach (var eventLine in eventLines) Console.WriteLine(LidGuardCommandTimestampFormatter.FormatHookEventLineForDisplay(eventLine));
            }

            if (providers.Count > 1) Console.WriteLine();
        }

        return exitCode;
    }

    private static int WriteProviderHookStatus(AgentProvider provider, IReadOnlyDictionary<string, string> options)
    {
        if (!TryCreateHookInstaller(provider, out var installer)) return WriteUnsupportedProvider();
        if (!TryCreateHookRequest(options, installer, out var request, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var inspection = installer.Inspect(request);
        WriteHookInspection(inspection);
        return 0;
    }

    private static int InstallProviderHook(AgentProvider provider, IReadOnlyDictionary<string, string> options)
    {
        if (!TryCreateHookInstaller(provider, out var installer)) return WriteUnsupportedProvider();
        if (!TryCreateHookRequest(options, installer, out var request, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var result = installer.Install(request);
        WriteHookInspection(result.Inspection);

        WriteHookManagementResult(result.BackupFilePath, result.Changed, result.Inspection.Provider, result.Message);
        return result.Succeeded ? 0 : 1;
    }

    private static int RemoveProviderHook(AgentProvider provider, IReadOnlyDictionary<string, string> options)
    {
        if (!TryCreateHookInstaller(provider, out var installer)) return WriteUnsupportedProvider();
        if (!TryCreateHookRequest(options, installer, out var request, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var result = installer.Remove(request);
        WriteHookInspection(result.Inspection);

        WriteHookManagementResult(result.BackupFilePath, result.Changed, result.Inspection.Provider, result.Message);
        return result.Succeeded ? 0 : 1;
    }

    private static bool TryCreateHookInstaller(AgentProvider provider, out IHookInstaller installer)
    {
        installer = provider switch
        {
            AgentProvider.Codex => new CodexHookInstaller(),
            AgentProvider.Claude => new ClaudeHookInstaller(),
            AgentProvider.GitHubCopilot => new GitHubCopilotHookInstaller(),
            _ => null
        };

        return installer is not null;
    }

    private static bool TryCreateHookRequest(
        IReadOnlyDictionary<string, string> options,
        IHookInstaller installer,
        out HookInstallationRequest request,
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

        message = LocalizationService.GetString("ManagementConfigCannotBeUsedWithAllProviders", "The config option cannot be used with all providers because each provider has a different configuration file.");
        return false;
    }

    private static bool TryParseMaximumLineCount(IReadOnlyDictionary<string, string> options, out int maximumLineCount, out string message)
    {
        maximumLineCount = 50;
        message = string.Empty;

        var countText = CommandOptionReader.GetOption(options, "count", "lines", "take");
        if (string.IsNullOrWhiteSpace(countText)) return true;
        if (int.TryParse(countText, out maximumLineCount) && maximumLineCount > 0) return true;

        message = LocalizationService.GetString("ManagementHookEventCountValidation", "The hook event count must be a positive integer.");
        return false;
    }

    private static void WriteHookManagementResult(string backupFilePath, bool changed, AgentProvider provider, string message)
    {
        if (!string.IsNullOrWhiteSpace(backupFilePath)) Console.WriteLine(LocalizationService.GetFormattedString("ManagementBackup", backupFilePath));
        Console.WriteLine(LocalizationService.GetFormattedString("ManagementChanged", LocalizationService.DisplayBoolean(changed)));
        Console.WriteLine(LocalizationService.GetFormattedString("ManagementMessage", DisplayHookManagementMessage(provider, message)));
    }

    private static void WriteHookInspection(HookInstallationInspection inspection)
    {
        switch (inspection.Provider)
        {
            case AgentProvider.Codex:
                WriteCodexHookInspection(inspection);
                break;
            case AgentProvider.Claude:
                WriteClaudeHookInspection(inspection);
                break;
            case AgentProvider.GitHubCopilot:
                WriteGitHubCopilotHookInspection(inspection);
                break;
            default:
                WriteUnsupportedProvider();
                break;
        }
    }

    private static void WriteCodexHookInspection(HookInstallationInspection inspection)
    {
        Console.WriteLine(LocalizationService.GetString("ManagementHookInstallationTitle"));
        WriteField("ManagementLabelProvider", "Provider", inspection.Provider);
        WriteField("ManagementLabelStatus", "Status", inspection.Status);
        WriteField("ManagementLabelInstalled", "Installed", inspection.IsInstalled);
        WriteField("ManagementLabelConfig", "Config", inspection.ConfigurationFilePath);
        WriteField("ManagementLabelConfigExists", "Config exists", inspection.ConfigurationFileExists);
        WriteField("ManagementLabelExecutable", "Executable", inspection.HookExecutablePath);
        WriteField("ManagementLabelCommand", "Command", inspection.HookCommand);
        WriteField("ManagementLabelHookLog", "Hook log", GetHookLogFilePath(inspection.Provider));
        WriteField("ManagementLabelFeatureFlag", "Feature flag", inspection.HasCheck(HookInstallationCheck.HooksFeatureFlag));
        WriteField("ManagementLabelManagedBlock", "Managed block", inspection.HasCheck(HookInstallationCheck.ManagedBlock));
        WriteField("ManagementLabelUserPromptSubmitHook", "UserPromptSubmit hook", inspection.HasCheck(HookInstallationCheck.UserPromptSubmitHook));
        WriteField("ManagementLabelStopHook", "Stop hook", inspection.HasCheck(HookInstallationCheck.StopHook));
        WriteField("ManagementLabelPermissionRequestHook", "PermissionRequest hook", inspection.HasCheck(HookInstallationCheck.PermissionRequestHook));
        WriteField("ManagementLabelRequiredStopHooks", "Required stop hooks", inspection.HasCheck(HookInstallationCheck.StopHook));
        WriteField("ManagementLabelOptionalSessionEndHook", "Optional SessionEnd hook", inspection.HasCheck(HookInstallationCheck.SessionEndHook));
        WriteField("ManagementLabelValidCommand", "Valid command", inspection.HasCheck(HookInstallationCheck.ValidHookCommand));
        WriteField("ManagementLabelExpectedCommand", "Expected command", inspection.HasCheck(HookInstallationCheck.ExpectedHookCommand));
        WriteField("ManagementLabelMessage", "Message", DisplayHookManagementMessage(inspection.Provider, inspection.Message));
    }

    private static void WriteClaudeHookInspection(HookInstallationInspection inspection)
    {
        Console.WriteLine(LocalizationService.GetString("ManagementHookInstallationTitle"));
        WriteField("ManagementLabelProvider", "Provider", inspection.Provider);
        WriteField("ManagementLabelStatus", "Status", inspection.Status);
        WriteField("ManagementLabelInstalled", "Installed", inspection.IsInstalled);
        WriteField("ManagementLabelConfig", "Config", inspection.ConfigurationFilePath);
        WriteField("ManagementLabelConfigExists", "Config exists", inspection.ConfigurationFileExists);
        WriteField("ManagementLabelExecutable", "Executable", inspection.HookExecutablePath);
        WriteField("ManagementLabelCommand", "Command", inspection.HookCommand);
        WriteField("ManagementLabelHookLog", "Hook log", GetHookLogFilePath(inspection.Provider));
        WriteField("ManagementLabelHooksObject", "Hooks object", inspection.HasCheck(HookInstallationCheck.HooksObject));
        WriteField("ManagementLabelManagedHooks", "Managed hooks", inspection.HasCheck(HookInstallationCheck.ManagedHookEntries));
        WriteField("ManagementLabelUserPromptSubmitHook", "UserPromptSubmit hook", inspection.HasCheck(HookInstallationCheck.UserPromptSubmitHook));
        WriteField("ManagementLabelPreToolUseHook", "PreToolUse hook", inspection.HasCheck(HookInstallationCheck.PreToolUseHook));
        WriteField("ManagementLabelPostToolUseHook", "PostToolUse hook", inspection.HasCheck(HookInstallationCheck.PostToolUseHook));
        WriteField("ManagementLabelPostToolUseFailureHook", "PostToolUseFailure hook", inspection.HasCheck(HookInstallationCheck.PostToolUseFailureHook));
        WriteField("ManagementLabelSubagentStartHook", "SubagentStart hook", inspection.HasCheck(HookInstallationCheck.SubagentStartHook));
        WriteField("ManagementLabelSubagentStopHook", "SubagentStop hook", inspection.HasCheck(HookInstallationCheck.SubagentStopHook));
        WriteField("ManagementLabelTaskCreatedHook", "TaskCreated hook", inspection.HasCheck(HookInstallationCheck.TaskCreatedHook));
        WriteField("ManagementLabelTaskCompletedHook", "TaskCompleted hook", inspection.HasCheck(HookInstallationCheck.TaskCompletedHook));
        WriteField("ManagementLabelStopHook", "Stop hook", inspection.HasCheck(HookInstallationCheck.StopHook));
        WriteField("ManagementLabelStopFailureHook", "StopFailure hook", inspection.HasCheck(HookInstallationCheck.StopFailureHook));
        WriteField("ManagementLabelElicitationHook", "Elicitation hook", inspection.HasCheck(HookInstallationCheck.ElicitationHook));
        WriteField("ManagementLabelPermissionRequestHook", "PermissionRequest hook", inspection.HasCheck(HookInstallationCheck.PermissionRequestHook));
        WriteField("ManagementLabelNotificationHook", "Notification hook", inspection.HasCheck(HookInstallationCheck.NotificationHook));
        WriteField("ManagementLabelSessionEndHook", "SessionEnd hook", inspection.HasCheck(HookInstallationCheck.SessionEndHook));
        WriteField("ManagementLabelAllStopHooks", "All stop hooks", HasClaudeAllStopHooks(inspection));
        WriteField("ManagementLabelExpectedCommand", "Expected command", inspection.HasCheck(HookInstallationCheck.ExpectedHookCommand));
        WriteField("ManagementLabelExpectedNotificationMatcher", "Expected notification matcher", inspection.HasCheck(HookInstallationCheck.ExpectedNotificationMatcher));
        WriteField("ManagementLabelExpectedShell", "Expected shell", inspection.HasCheck(HookInstallationCheck.ExpectedHookShell));
        WriteField("ManagementLabelMessage", "Message", DisplayHookManagementMessage(inspection.Provider, inspection.Message));
    }

    private static void WriteGitHubCopilotHookInspection(HookInstallationInspection inspection)
    {
        Console.WriteLine(LocalizationService.GetString("ManagementHookInstallationTitle"));
        WriteField("ManagementLabelProvider", "Provider", inspection.Provider);
        WriteField("ManagementLabelStatus", "Status", inspection.Status);
        WriteField("ManagementLabelInstalled", "Installed", inspection.IsInstalled);
        WriteField("ManagementLabelConfig", "Config", inspection.ConfigurationFilePath);
        WriteField("ManagementLabelConfigExists", "Config exists", inspection.ConfigurationFileExists);
        WriteField("ManagementLabelExecutable", "Executable", inspection.HookExecutablePath);
        WriteField("ManagementLabelCommand", "Command", inspection.HookCommand);
        WriteField("ManagementLabelHookLog", "Hook log", GetHookLogFilePath(inspection.Provider));
        WriteField("ManagementLabelHooksObject", "Hooks object", inspection.HasCheck(HookInstallationCheck.HooksObject));
        WriteField("ManagementLabelManagedHooks", "Managed hooks", inspection.HasCheck(HookInstallationCheck.ManagedHookEntries));
        WriteField("ManagementLabelSessionStartHook", "SessionStart hook", inspection.HasCheck(HookInstallationCheck.SessionStartHook));
        WriteField("ManagementLabelSessionEndHook", "SessionEnd hook", inspection.HasCheck(HookInstallationCheck.SessionEndHook));
        WriteField("ManagementLabelUserPromptSubmittedHook", "UserPromptSubmitted hook", inspection.HasCheck(HookInstallationCheck.UserPromptSubmittedHook));
        WriteField("ManagementLabelPreToolUseHook", "PreToolUse hook", inspection.HasCheck(HookInstallationCheck.PreToolUseHook));
        WriteField("ManagementLabelPostToolUseHook", "PostToolUse hook", inspection.HasCheck(HookInstallationCheck.PostToolUseHook));
        WriteField("ManagementLabelPermissionRequestHook", "PermissionRequest hook", inspection.HasCheck(HookInstallationCheck.PermissionRequestHook));
        WriteField("ManagementLabelAgentStopHook", "AgentStop hook", inspection.HasCheck(HookInstallationCheck.AgentStopHook));
        WriteField("ManagementLabelSubagentStartHook", "SubagentStart hook", inspection.HasCheck(HookInstallationCheck.SubagentStartHook));
        WriteField("ManagementLabelSubagentStopHook", "SubagentStop hook", inspection.HasCheck(HookInstallationCheck.SubagentStopHook));
        WriteField("ManagementLabelErrorOccurredHook", "ErrorOccurred hook", inspection.HasCheck(HookInstallationCheck.ErrorOccurredHook));
        WriteField("ManagementLabelNotificationHook", "Notification hook", inspection.HasCheck(HookInstallationCheck.NotificationHook));
        WriteField("ManagementLabelExpectedCommands", "Expected commands", inspection.HasCheck(HookInstallationCheck.ExpectedHookCommands));
        WriteField("ManagementLabelExpectedNotificationMatcher", "Expected notification matcher", inspection.HasCheck(HookInstallationCheck.ExpectedNotificationMatcher));
        WriteField("ManagementLabelConflictingAgentStopHooks", "Conflicting agentStop hooks", inspection.HasCheck(HookInstallationCheck.ConflictingAgentStopHooks));
        WriteField(
            "ManagementLabelConflictSources",
            "Conflict sources",
            inspection.ConflictingAgentStopHookSources.Count == 0 ? LocalizationService.GetString("TextDisplayNone") : string.Join(" | ", inspection.ConflictingAgentStopHookSources));
        WriteField("ManagementLabelMessage", "Message", DisplayHookManagementMessage(inspection.Provider, inspection.Message));
    }

    private static bool HasClaudeAllStopHooks(HookInstallationInspection inspection)
        => inspection.HasCheck(HookInstallationCheck.StopHook)
        && inspection.HasCheck(HookInstallationCheck.StopFailureHook)
        && inspection.HasCheck(HookInstallationCheck.SessionEndHook);

    private static string DisplayHookManagementMessage(AgentProvider provider, string message)
    {
        var providerName = ManagedProviderSelection.GetProviderDisplayName(provider);
        if (string.IsNullOrWhiteSpace(message)) return LocalizationService.GetString("TextDisplayNone");
        if (message.Equals($"{providerName} hook is installed.", StringComparison.Ordinal)) return LocalizationService.GetFormattedString("HookManagementIsInstalled", providerName);
        if (message.Equals($"{providerName} hook is installed but needs update.", StringComparison.Ordinal)) return LocalizationService.GetFormattedString("HookManagementInstalledNeedsUpdate", providerName);
        if (message.Equals($"{providerName} hook is not installed.", StringComparison.Ordinal)) return LocalizationService.GetFormattedString("HookManagementNotInstalled", providerName);
        if (message.Equals($"{providerName} hook is already installed.", StringComparison.Ordinal)) return LocalizationService.GetFormattedString("HookManagementAlreadyInstalled", providerName);
        if (message.Equals($"{providerName} hook is already installed outside the LidGuard managed block.", StringComparison.Ordinal)) return LocalizationService.GetFormattedString("HookManagementAlreadyInstalledOutsideManagedBlock", providerName);
        if (message.Equals($"{providerName} hook installed.", StringComparison.Ordinal)) return LocalizationService.GetFormattedString("HookManagementInstalled", providerName);
        if (message.Equals($"{providerName} hook configuration was written but still needs attention.", StringComparison.Ordinal)) return LocalizationService.GetFormattedString("HookManagementWrittenNeedsAttention", providerName);
        if (message.Equals($"No LidGuard-managed {providerName} hook was found.", StringComparison.Ordinal)) return LocalizationService.GetFormattedString("HookManagementNoManagedHookFound", providerName);
        if (message.Equals($"{providerName} hook removed.", StringComparison.Ordinal)) return LocalizationService.GetFormattedString("HookManagementRemoved", providerName);
        if (message.Equals($"Only {providerName} hook installation is implemented.", StringComparison.Ordinal)) return LocalizationService.GetFormattedString("HookManagementUnsupportedInstallation", providerName);
        if (message.Equals($"Only {providerName} hook removal is implemented.", StringComparison.Ordinal)) return LocalizationService.GetFormattedString("HookManagementUnsupportedRemoval", providerName);
        if (message.Equals($"{providerName} configuration file does not exist.", StringComparison.Ordinal)) return LocalizationService.GetFormattedString("HookManagementConfigurationFileDoesNotExist", providerName);
        if (message.Equals($"{providerName} settings file does not exist.", StringComparison.Ordinal)) return LocalizationService.GetFormattedString("HookManagementConfigurationFileDoesNotExist", providerName);
        if (message.Equals($"{providerName} hook configuration file does not exist.", StringComparison.Ordinal)) return LocalizationService.GetFormattedString("HookManagementConfigurationFileDoesNotExist", providerName);
        const string hookExecutableMissingPrefix = "Hook executable or command does not exist: ";
        if (message.StartsWith(hookExecutableMissingPrefix, StringComparison.Ordinal)) return LocalizationService.GetFormattedString("HookManagementHookExecutableDoesNotExist", message[hookExecutableMissingPrefix.Length..]);
        return message;
    }

    private static void WriteField(string labelResourceName, string fallbackLabel, object value)
    {
        var displayValue = value switch
        {
            bool booleanValue => LocalizationService.DisplayBoolean(booleanValue),
            HookInstallationStatus status => DisplayHookInstallationStatus(status),
            _ => LocalizationService.DisplayOptionalValue(value?.ToString() ?? string.Empty)
        };
        Console.WriteLine(LocalizationService.GetFormattedString("ManagementField", LocalizationService.GetString(labelResourceName, fallbackLabel), displayValue));
    }

    private static string DisplayHookInstallationStatus(HookInstallationStatus status)
        => LocalizationService.GetString($"DisplayHookInstallationStatus{status}", status.ToString());

    private static string GetHookLogFilePath(AgentProvider provider)
    {
        if (provider == AgentProvider.Codex) return CodexHookEventLog.GetDefaultLogFilePath();
        if (provider == AgentProvider.Claude) return ClaudeHookEventLog.GetDefaultLogFilePath();
        if (provider == AgentProvider.GitHubCopilot) return GitHubCopilotHookEventLog.GetDefaultLogFilePath();
        return string.Empty;
    }

    private static int WriteUnsupportedProvider()
    {
        Console.Error.WriteLine(LocalizationService.GetString("ManagementUnsupportedHookManagement"));
        return 1;
    }

}

