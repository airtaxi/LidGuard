using LidGuard.Hooks;
using LidGuard.Sessions;
using LidGuard.Localization;

namespace LidGuard.Commands;

internal static class HookManagementCommand
{
    public static int WriteHookStatus(IReadOnlyDictionary<string, string> options)
    {
        if (!TrySelectHookProviders(options, LocalizationService.GetString("ManagementPromptHookStatus"), true, out var selectedProviders, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        ManagedProviderSelection.ResolveAvailableProviders(selectedProviders, ManagedProviderConfigurationRoots.GetHookCandidatePaths, out var providers, out var skippedProviderMessages);

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
        if (!TrySelectHookProviders(options, LocalizationService.GetString("ManagementPromptHookInstall"), true, out var selectedProviders, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        ManagedProviderSelection.ResolveAvailableProviders(selectedProviders, ManagedProviderConfigurationRoots.GetHookCandidatePaths, out var providers, out var skippedProviderMessages);

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
        if (!TrySelectHookProviders(options, LocalizationService.GetString("ManagementPromptHookRemove"), true, out var selectedProviders, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        ManagedProviderSelection.ResolveAvailableProviders(selectedProviders, ManagedProviderConfigurationRoots.GetHookCandidatePaths, out var providers, out var skippedProviderMessages);

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
        if (!TrySelectHookProviders(options, LocalizationService.GetString("ManagementPromptHookEvents"), false, out var selectedProviders, out var providerMessage))
        {
            Console.Error.WriteLine(providerMessage);
            return 1;
        }

        if (!TryParseMaximumLineCount(options, out var maximumLineCount, out var lineCountMessage))
        {
            Console.Error.WriteLine(lineCountMessage);
            return 1;
        }

        ManagedProviderSelection.ResolveAvailableProviders(selectedProviders, ManagedProviderConfigurationRoots.GetHookCandidatePaths, out var providers, out var skippedProviderMessages);

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
            if (eventLines.Count == 0) Console.WriteLine(LocalizationService.GetString("TextDisplayEmpty"));
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

    private static bool TryCreateHookRequest(IReadOnlyDictionary<string, string> options, IHookInstaller installer, out HookInstallationRequest request, out string message)
    {
        request = null;
        message = string.Empty;

        var configurationFilePath = CommandOptionReader.GetOption(options, "config", "configuration", "configuration-file");
        request = installer.CreateDefaultRequest(configurationFilePath);
        return true;
    }

    private static bool TrySelectHookProviders(IReadOnlyDictionary<string, string> options, string prompt, bool rejectSharedConfigurationFile, out IReadOnlyList<AgentProvider> providers, out string message)
    {
        providers = [];
        message = string.Empty;

        if (!ManagedProviderSelection.TrySelectProviders(options, prompt, out providers, out message)) return false;
        if (!rejectSharedConfigurationFile || providers.Count < 2 || string.IsNullOrWhiteSpace(CommandOptionReader.GetOption(options, "config", "configuration", "configuration-file"))) return true;

        message = LocalizationService.GetString("ManagementConfigCannotBeUsedWithAllProviders");
        return false;
    }

    private static bool TryParseMaximumLineCount(IReadOnlyDictionary<string, string> options, out int maximumLineCount, out string message)
    {
        maximumLineCount = 50;
        message = string.Empty;

        var countText = CommandOptionReader.GetOption(options, "count", "lines", "take");
        if (string.IsNullOrWhiteSpace(countText)) return true;
        if (int.TryParse(countText, out maximumLineCount) && maximumLineCount > 0) return true;

        message = LocalizationService.GetString("ManagementHookEventCountValidation");
        return false;
    }

    internal static void WriteHookManagementResult(string backupFilePath, bool changed, AgentProvider provider, string message)
    {
        if (!string.IsNullOrWhiteSpace(backupFilePath)) Console.WriteLine(LocalizationService.GetFormattedString("ManagementBackup", backupFilePath));
        Console.WriteLine(LocalizationService.GetFormattedString("ManagementChanged", LocalizationService.DisplayBoolean(changed)));
        Console.WriteLine(LocalizationService.GetFormattedString("ManagementMessage", DisplayHookManagementMessage(provider, message)));
    }

    internal static void WriteHookInspection(HookInstallationInspection inspection)
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
        ManagementFieldWriter.WriteField("ManagementLabelProvider", inspection.Provider);
        ManagementFieldWriter.WriteField("ManagementLabelStatus", inspection.Status);
        ManagementFieldWriter.WriteField("ManagementLabelInstalled", inspection.IsInstalled);
        ManagementFieldWriter.WriteField("ManagementLabelConfig", inspection.ConfigurationFilePath);
        ManagementFieldWriter.WriteField("ManagementLabelConfigExists", inspection.ConfigurationFileExists);
        ManagementFieldWriter.WriteField("ManagementLabelExecutable", inspection.HookExecutablePath);
        ManagementFieldWriter.WriteField("ManagementLabelCommand", inspection.HookCommand);
        ManagementFieldWriter.WriteField("ManagementLabelHookLog", GetHookLogFilePath(inspection.Provider));
        ManagementFieldWriter.WriteField("ManagementLabelFeatureFlag", inspection.HasCheck(HookInstallationCheck.HooksFeatureFlag));
        ManagementFieldWriter.WriteField("ManagementLabelManagedBlock", inspection.HasCheck(HookInstallationCheck.ManagedBlock));
        ManagementFieldWriter.WriteField("ManagementLabelUserPromptSubmitHook", inspection.HasCheck(HookInstallationCheck.UserPromptSubmitHook));
        ManagementFieldWriter.WriteField("ManagementLabelStopHook", inspection.HasCheck(HookInstallationCheck.StopHook));
        ManagementFieldWriter.WriteField("ManagementLabelPermissionRequestHook", inspection.HasCheck(HookInstallationCheck.PermissionRequestHook));
        ManagementFieldWriter.WriteField("ManagementLabelRequiredStopHooks", inspection.HasCheck(HookInstallationCheck.StopHook));
        ManagementFieldWriter.WriteField("ManagementLabelOptionalSessionEndHook", inspection.HasCheck(HookInstallationCheck.SessionEndHook));
        ManagementFieldWriter.WriteField("ManagementLabelValidCommand", inspection.HasCheck(HookInstallationCheck.ValidHookCommand));
        ManagementFieldWriter.WriteField("ManagementLabelExpectedCommand", inspection.HasCheck(HookInstallationCheck.ExpectedHookCommand));
        ManagementFieldWriter.WriteField("ManagementLabelMessage", DisplayHookManagementMessage(inspection.Provider, inspection.Message));
    }

    private static void WriteClaudeHookInspection(HookInstallationInspection inspection)
    {
        Console.WriteLine(LocalizationService.GetString("ManagementHookInstallationTitle"));
        ManagementFieldWriter.WriteField("ManagementLabelProvider", inspection.Provider);
        ManagementFieldWriter.WriteField("ManagementLabelStatus", inspection.Status);
        ManagementFieldWriter.WriteField("ManagementLabelInstalled", inspection.IsInstalled);
        ManagementFieldWriter.WriteField("ManagementLabelConfig", inspection.ConfigurationFilePath);
        ManagementFieldWriter.WriteField("ManagementLabelConfigExists", inspection.ConfigurationFileExists);
        ManagementFieldWriter.WriteField("ManagementLabelExecutable", inspection.HookExecutablePath);
        ManagementFieldWriter.WriteField("ManagementLabelCommand", inspection.HookCommand);
        ManagementFieldWriter.WriteField("ManagementLabelHookLog", GetHookLogFilePath(inspection.Provider));
        ManagementFieldWriter.WriteField("ManagementLabelHooksObject", inspection.HasCheck(HookInstallationCheck.HooksObject));
        ManagementFieldWriter.WriteField("ManagementLabelManagedHooks", inspection.HasCheck(HookInstallationCheck.ManagedHookEntries));
        ManagementFieldWriter.WriteField("ManagementLabelUserPromptSubmitHook", inspection.HasCheck(HookInstallationCheck.UserPromptSubmitHook));
        ManagementFieldWriter.WriteField("ManagementLabelPreToolUseHook", inspection.HasCheck(HookInstallationCheck.PreToolUseHook));
        ManagementFieldWriter.WriteField("ManagementLabelPostToolUseHook", inspection.HasCheck(HookInstallationCheck.PostToolUseHook));
        ManagementFieldWriter.WriteField("ManagementLabelPostToolUseFailureHook", inspection.HasCheck(HookInstallationCheck.PostToolUseFailureHook));
        ManagementFieldWriter.WriteField("ManagementLabelSubagentStartHook", inspection.HasCheck(HookInstallationCheck.SubagentStartHook));
        ManagementFieldWriter.WriteField("ManagementLabelSubagentStopHook", inspection.HasCheck(HookInstallationCheck.SubagentStopHook));
        ManagementFieldWriter.WriteField("ManagementLabelTaskCreatedHook", inspection.HasCheck(HookInstallationCheck.TaskCreatedHook));
        ManagementFieldWriter.WriteField("ManagementLabelTaskCompletedHook", inspection.HasCheck(HookInstallationCheck.TaskCompletedHook));
        ManagementFieldWriter.WriteField("ManagementLabelStopHook", inspection.HasCheck(HookInstallationCheck.StopHook));
        ManagementFieldWriter.WriteField("ManagementLabelStopFailureHook", inspection.HasCheck(HookInstallationCheck.StopFailureHook));
        ManagementFieldWriter.WriteField("ManagementLabelElicitationHook", inspection.HasCheck(HookInstallationCheck.ElicitationHook));
        ManagementFieldWriter.WriteField("ManagementLabelPermissionRequestHook", inspection.HasCheck(HookInstallationCheck.PermissionRequestHook));
        ManagementFieldWriter.WriteField("ManagementLabelNotificationHook", inspection.HasCheck(HookInstallationCheck.NotificationHook));
        ManagementFieldWriter.WriteField("ManagementLabelSessionEndHook", inspection.HasCheck(HookInstallationCheck.SessionEndHook));
        ManagementFieldWriter.WriteField("ManagementLabelAllStopHooks", HasClaudeAllStopHooks(inspection));
        ManagementFieldWriter.WriteField("ManagementLabelExpectedCommand", inspection.HasCheck(HookInstallationCheck.ExpectedHookCommand));
        ManagementFieldWriter.WriteField("ManagementLabelExpectedNotificationMatcher", inspection.HasCheck(HookInstallationCheck.ExpectedNotificationMatcher));
        ManagementFieldWriter.WriteField("ManagementLabelExpectedShell", inspection.HasCheck(HookInstallationCheck.ExpectedHookShell));
        ManagementFieldWriter.WriteField("ManagementLabelMessage", DisplayHookManagementMessage(inspection.Provider, inspection.Message));
    }

    private static void WriteGitHubCopilotHookInspection(HookInstallationInspection inspection)
    {
        Console.WriteLine(LocalizationService.GetString("ManagementHookInstallationTitle"));
        ManagementFieldWriter.WriteField("ManagementLabelProvider", inspection.Provider);
        ManagementFieldWriter.WriteField("ManagementLabelStatus", inspection.Status);
        ManagementFieldWriter.WriteField("ManagementLabelInstalled", inspection.IsInstalled);
        ManagementFieldWriter.WriteField("ManagementLabelConfig", inspection.ConfigurationFilePath);
        ManagementFieldWriter.WriteField("ManagementLabelConfigExists", inspection.ConfigurationFileExists);
        ManagementFieldWriter.WriteField("ManagementLabelExecutable", inspection.HookExecutablePath);
        ManagementFieldWriter.WriteField("ManagementLabelCommand", inspection.HookCommand);
        ManagementFieldWriter.WriteField("ManagementLabelHookLog", GetHookLogFilePath(inspection.Provider));
        ManagementFieldWriter.WriteField("ManagementLabelHooksObject", inspection.HasCheck(HookInstallationCheck.HooksObject));
        ManagementFieldWriter.WriteField("ManagementLabelManagedHooks", inspection.HasCheck(HookInstallationCheck.ManagedHookEntries));
        ManagementFieldWriter.WriteField("ManagementLabelSessionStartHook", inspection.HasCheck(HookInstallationCheck.SessionStartHook));
        ManagementFieldWriter.WriteField("ManagementLabelSessionEndHook", inspection.HasCheck(HookInstallationCheck.SessionEndHook));
        ManagementFieldWriter.WriteField("ManagementLabelUserPromptSubmittedHook", inspection.HasCheck(HookInstallationCheck.UserPromptSubmittedHook));
        ManagementFieldWriter.WriteField("ManagementLabelPreToolUseHook", inspection.HasCheck(HookInstallationCheck.PreToolUseHook));
        ManagementFieldWriter.WriteField("ManagementLabelPostToolUseHook", inspection.HasCheck(HookInstallationCheck.PostToolUseHook));
        ManagementFieldWriter.WriteField("ManagementLabelPermissionRequestHook", inspection.HasCheck(HookInstallationCheck.PermissionRequestHook));
        ManagementFieldWriter.WriteField("ManagementLabelAgentStopHook", inspection.HasCheck(HookInstallationCheck.AgentStopHook));
        ManagementFieldWriter.WriteField("ManagementLabelSubagentStartHook", inspection.HasCheck(HookInstallationCheck.SubagentStartHook));
        ManagementFieldWriter.WriteField("ManagementLabelSubagentStopHook", inspection.HasCheck(HookInstallationCheck.SubagentStopHook));
        ManagementFieldWriter.WriteField("ManagementLabelErrorOccurredHook", inspection.HasCheck(HookInstallationCheck.ErrorOccurredHook));
        ManagementFieldWriter.WriteField("ManagementLabelNotificationHook", inspection.HasCheck(HookInstallationCheck.NotificationHook));
        ManagementFieldWriter.WriteField("ManagementLabelExpectedCommands", inspection.HasCheck(HookInstallationCheck.ExpectedHookCommands));
        ManagementFieldWriter.WriteField("ManagementLabelExpectedNotificationMatcher", inspection.HasCheck(HookInstallationCheck.ExpectedNotificationMatcher));
        ManagementFieldWriter.WriteField("ManagementLabelConflictingAgentStopHooks", inspection.HasCheck(HookInstallationCheck.ConflictingAgentStopHooks));
        ManagementFieldWriter.WriteField("ManagementLabelConflictSources", inspection.ConflictingAgentStopHookSources.Count == 0 ? LocalizationService.GetString("TextDisplayNone") : string.Join(" | ", inspection.ConflictingAgentStopHookSources));
        ManagementFieldWriter.WriteField("ManagementLabelMessage", DisplayHookManagementMessage(inspection.Provider, inspection.Message));
    }

    private static bool HasClaudeAllStopHooks(HookInstallationInspection inspection)
        => inspection.HasCheck(HookInstallationCheck.StopHook) && inspection.HasCheck(HookInstallationCheck.StopFailureHook) && inspection.HasCheck(HookInstallationCheck.SessionEndHook);

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

