using LidGuard.Hooks;
using LidGuard.Localization;
using LidGuard.Sessions;

namespace LidGuard.Commands;

internal static class WslHookManagementCommand
{
    public static int WriteHookStatus(IReadOnlyDictionary<string, string> options)
    {
        if (!WslCommandUtilities.TryCreateContext(options, out var context, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        if (!TrySelectHookProviders(options, LocalizationService.GetString("ManagementPromptHookStatus"), true, context.DistroName, out var providers, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var exitCode = 0;
        foreach (var provider in providers)
        {
            if (providers.Count > 1) Console.WriteLine(LocalizationService.GetFormattedString("ManagementHookStatusTitle", provider));
            var providerExitCode = WriteProviderHookStatus(provider, options, context);
            if (providerExitCode != 0) exitCode = providerExitCode;
            if (providers.Count > 1) Console.WriteLine();
        }

        return exitCode;
    }

    public static int InstallHook(IReadOnlyDictionary<string, string> options)
    {
        if (!WslCommandUtilities.TryCreateContext(options, out var context, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        if (!TrySelectHookProviders(options, LocalizationService.GetString("ManagementPromptHookInstall"), true, context.DistroName, out var providers, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var exitCode = 0;
        foreach (var provider in providers)
        {
            if (providers.Count > 1) Console.WriteLine(LocalizationService.GetFormattedString("ManagementInstallingHook", provider));
            var providerExitCode = InstallProviderHook(provider, options, context);
            if (providerExitCode != 0) exitCode = providerExitCode;
        }

        return exitCode;
    }

    public static int RemoveHook(IReadOnlyDictionary<string, string> options)
    {
        if (!WslCommandUtilities.TryCreateContext(options, out var context, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        if (!TrySelectHookProviders(options, LocalizationService.GetString("ManagementPromptHookRemove"), true, context.DistroName, out var providers, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var exitCode = 0;
        foreach (var provider in providers)
        {
            if (providers.Count > 1) Console.WriteLine(LocalizationService.GetFormattedString("ManagementRemovingHook", provider));
            var providerExitCode = RemoveProviderHook(provider, options, context);
            if (providerExitCode != 0) exitCode = providerExitCode;
        }

        return exitCode;
    }

    private static int WriteProviderHookStatus(AgentProvider provider, IReadOnlyDictionary<string, string> options, WslCommandUtilities.WslContext context)
    {
        if (!TryCreateInspection(provider, options, context, out var inspection, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        HookManagementCommand.WriteHookInspection(inspection);
        return 0;
    }

    private static int InstallProviderHook(AgentProvider provider, IReadOnlyDictionary<string, string> options, WslCommandUtilities.WslContext context)
    {
        if (!TryCreateInspection(provider, options, context, out var currentInspection, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        if (ShouldSkipInstall(currentInspection, out message))
        {
            HookManagementCommand.WriteHookInspection(currentInspection);
            HookManagementCommand.WriteHookManagementResult(string.Empty, false, currentInspection.Provider, message);
            return 0;
        }

        var originalContent = currentInspection.ConfigurationFileExists ? ReadRequiredConfigurationContent(context.DistroName, currentInspection.ConfigurationFilePath) : string.Empty;
        if (!TryCreateInstalledContent(provider, originalContent, currentInspection.HookCommand, out var updatedContent, out message))
        {
            HookManagementCommand.WriteHookInspection(currentInspection);
            HookManagementCommand.WriteHookManagementResult(string.Empty, false, currentInspection.Provider, message);
            return 1;
        }

        if (string.Equals(originalContent, updatedContent, StringComparison.Ordinal))
        {
            HookManagementCommand.WriteHookInspection(currentInspection);
            HookManagementCommand.WriteHookManagementResult(string.Empty, false, currentInspection.Provider, CreateAlreadyInstalledMessage(provider));
            return 0;
        }

        var backupFilePath = string.Empty;
        if (currentInspection.ConfigurationFileExists)
        {
            backupFilePath = WslCommandUtilities.CreateBackupFilePath(currentInspection.ConfigurationFilePath);
            if (!WslCommandUtilities.TryCopyFile(context.DistroName, currentInspection.ConfigurationFilePath, backupFilePath, out message))
            {
                Console.Error.WriteLine(message);
                return 1;
            }
        }

        var removedFile = currentInspection.Provider == AgentProvider.OpenCode && string.IsNullOrWhiteSpace(updatedContent);
        var updateSucceeded = removedFile ? WslCommandUtilities.TryRemoveFile(context.DistroName, currentInspection.ConfigurationFilePath, out message) : WslCommandUtilities.TryWriteTextFile(context.DistroName, currentInspection.ConfigurationFilePath, updatedContent, out message);
        if (!updateSucceeded)
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        if (!TryCreateInspection(provider, options, context, out var inspection, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        HookManagementCommand.WriteHookInspection(inspection);
        HookManagementCommand.WriteHookManagementResult(backupFilePath, true, inspection.Provider, inspection.IsInstalled ? CreateInstalledMessage(provider) : CreateWrittenNeedsAttentionMessage(provider));
        return inspection.IsInstalled ? 0 : 1;
    }

    private static int RemoveProviderHook(AgentProvider provider, IReadOnlyDictionary<string, string> options, WslCommandUtilities.WslContext context)
    {
        if (!TryCreateInspection(provider, options, context, out var currentInspection, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        if (!currentInspection.ConfigurationFileExists)
        {
            HookManagementCommand.WriteHookInspection(currentInspection);
            HookManagementCommand.WriteHookManagementResult(string.Empty, false, currentInspection.Provider, CreateNotInstalledMessage(provider));
            return 0;
        }

        var originalContent = ReadRequiredConfigurationContent(context.DistroName, currentInspection.ConfigurationFilePath);
        if (!TryCreateRemovedContent(provider, originalContent, out var updatedContent, out var changed, out message))
        {
            HookManagementCommand.WriteHookInspection(currentInspection);
            HookManagementCommand.WriteHookManagementResult(string.Empty, false, currentInspection.Provider, message);
            return 1;
        }

        if (!changed)
        {
            HookManagementCommand.WriteHookInspection(currentInspection);
            HookManagementCommand.WriteHookManagementResult(string.Empty, false, currentInspection.Provider, CreateNoManagedHookFoundMessage(provider));
            return 0;
        }

        var backupFilePath = WslCommandUtilities.CreateBackupFilePath(currentInspection.ConfigurationFilePath);
        if (!WslCommandUtilities.TryCopyFile(context.DistroName, currentInspection.ConfigurationFilePath, backupFilePath, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var removedFile = currentInspection.Provider == AgentProvider.OpenCode && string.IsNullOrWhiteSpace(updatedContent);
        var updateSucceeded = removedFile ? WslCommandUtilities.TryRemoveFile(context.DistroName, currentInspection.ConfigurationFilePath, out message) : WslCommandUtilities.TryWriteTextFile(context.DistroName, currentInspection.ConfigurationFilePath, updatedContent, out message);
        if (!updateSucceeded)
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        if (!TryCreateInspection(provider, options, context, out var inspection, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        HookManagementCommand.WriteHookInspection(inspection);
        HookManagementCommand.WriteHookManagementResult(backupFilePath, true, inspection.Provider, CreateRemovedMessage(provider));
        return 0;
    }

    private static bool TryCreateInspection(AgentProvider provider, IReadOnlyDictionary<string, string> options, WslCommandUtilities.WslContext context, out HookInstallationInspection inspection, out string message)
    {
        inspection = new HookInstallationInspection();
        var configuredConfigurationFilePath = CommandOptionReader.GetOption(options, "config", "configuration", "configuration-file");
        if (!WslProviderConfigurationRoots.TryGetHookConfigurationFilePath(context.DistroName, provider, configuredConfigurationFilePath, out var configurationFilePath, out message)) return false;

        var hookCommandName = WslCommandUtilities.GetHookCommandName(provider);
        if (string.IsNullOrWhiteSpace(hookCommandName))
        {
            message = LocalizationService.GetString("ManagementUnsupportedHookManagement");
            return false;
        }

        var hookCommand = WslCommandUtilities.CreateWslLidGuardCommand(context.WslExecutablePath, hookCommandName);
        var configurationFileExists = WslCommandUtilities.FileExists(context.DistroName, configurationFilePath);
        var content = string.Empty;
        if (configurationFileExists && !WslCommandUtilities.TryReadTextFile(context.DistroName, configurationFilePath, out content, out message)) return false;

        inspection = provider switch
        {
            AgentProvider.Codex => CodexHookConfigTomlDocument.InspectConfigToml(configurationFilePath, context.WslExecutablePath, hookCommand, content, configurationFileExists),
            AgentProvider.Claude => ClaudeHookSettingsJsonDocument.InspectSettingsJson(configurationFilePath, context.WslExecutablePath, hookCommand, content, configurationFileExists, HookCommandUtilities.BashShellName),
            AgentProvider.GitHubCopilot => CreateGitHubCopilotInspection(configurationFilePath, context.WslExecutablePath, hookCommand, content, configurationFileExists),
            AgentProvider.OpenCode => OpenCodeHookPluginDocument.InspectPlugin(configurationFilePath, context.WslExecutablePath, hookCommand, content, configurationFileExists),
            _ => new HookInstallationInspection()
        };

        if (inspection.Provider != AgentProvider.Unknown) return true;

        message = LocalizationService.GetString("ManagementUnsupportedHookManagement");
        return false;
    }

    private static HookInstallationInspection CreateGitHubCopilotInspection(string configurationFilePath, string hookExecutablePath, string hookCommand, string content, bool configurationFileExists)
    {
        var hookCommandsByEvent = GitHubCopilotHookConfigurationJsonDocument.CreateManagedHookCommands(hookCommand);
        return GitHubCopilotHookConfigurationJsonDocument.InspectConfigurationJson(configurationFilePath, hookExecutablePath, hookCommand, hookCommandsByEvent, content, configurationFileExists, HookCommandUtilities.BashShellName);
    }

    private static bool TryCreateInstalledContent(AgentProvider provider, string originalContent, string hookCommand, out string updatedContent, out string message)
    {
        updatedContent = string.Empty;
        message = string.Empty;

        switch (provider)
        {
            case AgentProvider.Codex:
                updatedContent = CodexHookConfigTomlDocument.InstallManagedHookBlock(originalContent, hookCommand);
                return true;
            case AgentProvider.Claude:
                return ClaudeHookSettingsJsonDocument.TryInstallManagedHooks(originalContent, hookCommand, HookCommandUtilities.BashShellName, out updatedContent, out message);
            case AgentProvider.GitHubCopilot:
                var hookCommandsByEvent = GitHubCopilotHookConfigurationJsonDocument.CreateManagedHookCommands(hookCommand);
                return GitHubCopilotHookConfigurationJsonDocument.TryInstallManagedHooks(originalContent, hookCommandsByEvent, HookCommandUtilities.BashShellName, out updatedContent, out message);
            case AgentProvider.OpenCode:
                updatedContent = OpenCodeHookPluginDocument.InstallManagedPlugin(hookCommand);
                return true;
            default:
                message = LocalizationService.GetString("ManagementUnsupportedHookManagement");
                return false;
        }
    }

    private static bool TryCreateRemovedContent(AgentProvider provider, string originalContent, out string updatedContent, out bool changed, out string message)
    {
        updatedContent = originalContent;
        changed = false;
        message = string.Empty;

        return provider switch
        {
            AgentProvider.Codex => TryCreateRemovedCodexContent(originalContent, out updatedContent, out changed, out message),
            AgentProvider.Claude => ClaudeHookSettingsJsonDocument.TryRemoveManagedHooks(originalContent, out updatedContent, out changed, out message),
            AgentProvider.GitHubCopilot => GitHubCopilotHookConfigurationJsonDocument.TryRemoveManagedHooks(originalContent, out updatedContent, out changed, out message),
            AgentProvider.OpenCode => OpenCodeHookPluginDocument.TryRemoveManagedPlugin(originalContent, out updatedContent, out changed, out message),
            _ => UnsupportedProvider(out updatedContent, out changed, out message)
        };
    }

    private static bool TrySelectHookProviders(IReadOnlyDictionary<string, string> options, string prompt, bool rejectSharedConfigurationFile, string distroName, out IReadOnlyList<AgentProvider> providers, out string message)
    {
        providers = [];
        message = string.Empty;

        if (!ManagedProviderSelection.TrySelectProviders(options, prompt, out var selectedProviders, out message)) return false;
        if (rejectSharedConfigurationFile && selectedProviders.Count > 1 && !string.IsNullOrWhiteSpace(CommandOptionReader.GetOption(options, "config", "configuration", "configuration-file")))
        {
            message = LocalizationService.GetString("ManagementConfigCannotBeUsedWithAllProviders");
            return false;
        }

        WslManagedProviderSelection.ResolveAvailableProviders(distroName, selectedProviders, WslManagedProviderSelection.TryHasHookProviderConfigurationRoot, out providers, out var skippedProviderMessages);

        ManagedProviderSelection.WriteSkippedProviderMessages(skippedProviderMessages);
        if (providers.Count > 0) return true;

        ManagedProviderSelection.WriteNoAvailableProvidersFound();
        return true;
    }

    private static bool ShouldSkipInstall(HookInstallationInspection inspection, out string message)
    {
        message = LocalizationService.GetFormattedString("HookManagementAlreadyInstalledOutsideManagedBlock", ManagedProviderSelection.GetProviderDisplayName(inspection.Provider));
        return inspection.Provider == AgentProvider.Codex && inspection.IsInstalled && !inspection.HasCheck(HookInstallationCheck.ManagedBlock);
    }

    private static string ReadRequiredConfigurationContent(string distroName, string configurationFilePath)
    {
        WslCommandUtilities.TryReadTextFile(distroName, configurationFilePath, out var content, out _);
        return content;
    }

    private static bool TryCreateRemovedCodexContent(string originalContent, out string updatedContent, out bool changed, out string message)
    {
        updatedContent = CodexHookConfigTomlDocument.RemoveManagedHookBlock(originalContent);
        changed = !string.Equals(originalContent, updatedContent, StringComparison.Ordinal);
        message = string.Empty;
        return true;
    }

    private static bool UnsupportedProvider(out string updatedContent, out bool changed, out string message)
    {
        updatedContent = string.Empty;
        changed = false;
        message = LocalizationService.GetString("ManagementUnsupportedHookManagement");
        return false;
    }

    private static string CreateAlreadyInstalledMessage(AgentProvider provider) => $"{ManagedProviderSelection.GetProviderDisplayName(provider)} hook is already installed.";

    private static string CreateInstalledMessage(AgentProvider provider) => $"{ManagedProviderSelection.GetProviderDisplayName(provider)} hook installed.";

    private static string CreateNoManagedHookFoundMessage(AgentProvider provider) => $"No LidGuard-managed {ManagedProviderSelection.GetProviderDisplayName(provider)} hook was found.";

    private static string CreateNotInstalledMessage(AgentProvider provider) => $"{ManagedProviderSelection.GetProviderDisplayName(provider)} hook is not installed.";

    private static string CreateRemovedMessage(AgentProvider provider) => $"{ManagedProviderSelection.GetProviderDisplayName(provider)} hook removed.";

    private static string CreateWrittenNeedsAttentionMessage(AgentProvider provider) => $"{ManagedProviderSelection.GetProviderDisplayName(provider)} hook configuration was written but still needs attention.";

}
