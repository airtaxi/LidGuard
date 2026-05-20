using LidGuard.Mcp;
using LidGuard.Sessions;
using LidGuard.Hooks;
using LidGuard.Localization;

namespace LidGuard.Commands;

internal static class McpManagementCommand
{
    public static int WriteMcpStatus(string providerText)
    {
        if (!ManagedProviderSelection.TrySelectProviders(providerText, LocalizationService.GetString("ManagementPromptMcpStatus", "Show MCP server status for provider"), out var selectedProviders, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        ManagedProviderSelection.ResolveAvailableProviders(
            selectedProviders,
            ManagedProviderConfigurationRoots.GetMcpCandidatePaths,
            out var providers,
            out var skippedProviderMessages);

        ManagedProviderSelection.WriteSkippedProviderMessages(skippedProviderMessages);
        if (providers.Count == 0) return ManagedProviderSelection.WriteNoAvailableProvidersFound();

        var exitCode = 0;
        foreach (var provider in providers)
        {
            if (providers.Count > 1) Console.WriteLine(LocalizationService.GetFormattedString("ManagementMcpStatusTitle", ManagedProviderSelection.GetProviderDisplayName(provider)));

            var providerExitCode = TryInspectProviderMcp(provider, out var inspectionResult)
                ? ManagedMcpInspectionResult.WriteProviderMcpStatus(inspectionResult)
                : WriteUnsupportedProvider();

            if (providerExitCode != 0) exitCode = providerExitCode;
            if (providers.Count > 1) Console.WriteLine();
        }

        return exitCode;
    }

    public static int InstallMcp(string providerText)
    {
        if (!ManagedProviderSelection.TrySelectProviders(providerText, LocalizationService.GetString("ManagementPromptMcpInstall", "Install MCP server for provider"), out var selectedProviders, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        ManagedProviderSelection.ResolveAvailableProviders(
            selectedProviders,
            ManagedProviderConfigurationRoots.GetMcpCandidatePaths,
            out var providers,
            out var skippedProviderMessages);

        ManagedProviderSelection.WriteSkippedProviderMessages(skippedProviderMessages);
        if (providers.Count == 0) return ManagedProviderSelection.WriteNoAvailableProvidersFound();

        var managedExecutableReference = HookCommandUtilities.GetDefaultMcpExecutableReference();
        if (!HookCommandUtilities.HookExecutableExists(managedExecutableReference))
        {
            Console.Error.WriteLine(LocalizationService.GetString("ManagementLidGuardExecutableMissing", "LidGuard executable or command does not exist: {0}").Replace("{0}", managedExecutableReference, StringComparison.Ordinal));
            return 1;
        }

        var exitCode = 0;
        foreach (var provider in providers)
        {
            if (providers.Count > 1) Console.WriteLine(LocalizationService.GetFormattedString("ManagementInstallingMcpServer", ManagedProviderSelection.GetProviderDisplayName(provider)));
            var providerExitCode = TryInspectProviderMcp(provider, out var inspectionResult)
                ? InstallProviderMcp(provider, managedExecutableReference, inspectionResult)
                : WriteUnsupportedProvider();
            if (providerExitCode != 0) exitCode = providerExitCode;
            if (providers.Count > 1) Console.WriteLine();
        }

        return exitCode;
    }

    public static int RemoveMcp(string providerText)
    {
        if (!ManagedProviderSelection.TrySelectProviders(providerText, LocalizationService.GetString("ManagementPromptMcpRemove", "Remove MCP server for provider"), out var selectedProviders, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        ManagedProviderSelection.ResolveAvailableProviders(
            selectedProviders,
            ManagedProviderConfigurationRoots.GetMcpCandidatePaths,
            out var providers,
            out var skippedProviderMessages);

        ManagedProviderSelection.WriteSkippedProviderMessages(skippedProviderMessages);
        if (providers.Count == 0) return ManagedProviderSelection.WriteNoAvailableProvidersFound();

        var exitCode = 0;
        foreach (var provider in providers)
        {
            if (providers.Count > 1) Console.WriteLine(LocalizationService.GetFormattedString("ManagementRemovingMcpServer", ManagedProviderSelection.GetProviderDisplayName(provider)));
            var providerExitCode = RemoveProviderMcp(provider);
            if (providerExitCode != 0) exitCode = providerExitCode;
            if (providers.Count > 1) Console.WriteLine();
        }

        return exitCode;
    }

    internal static IReadOnlyList<string> CreateProviderMcpInstallArguments(AgentProvider provider, string managedExecutableReference)
    {
        return provider switch
        {
            AgentProvider.Codex => ["mcp", "add", McpConfigurationTomlUtilities.ManagedMcpServerName, "--", managedExecutableReference, LidGuardMcpServerCommand.CommandName],
            AgentProvider.Claude => ["mcp", "add", "--scope", "user", McpConfigurationTomlUtilities.ManagedMcpServerName, "--", managedExecutableReference, LidGuardMcpServerCommand.CommandName],
            AgentProvider.GitHubCopilot => ["mcp", "add", McpConfigurationTomlUtilities.ManagedMcpServerName, "--", managedExecutableReference, LidGuardMcpServerCommand.CommandName],
            _ => []
        };
    }

    internal static IReadOnlyList<string> CreateProviderMcpRemoveArguments(AgentProvider provider)
    {
        return provider switch
        {
            AgentProvider.Codex => ["mcp", "remove", McpConfigurationTomlUtilities.ManagedMcpServerName],
            AgentProvider.Claude => ["mcp", "remove", "--scope", "user", McpConfigurationTomlUtilities.ManagedMcpServerName],
            AgentProvider.GitHubCopilot => ["mcp", "remove", McpConfigurationTomlUtilities.ManagedMcpServerName],
            _ => []
        };
    }

    private static int InstallProviderMcp(
        AgentProvider provider,
        string managedExecutableReference,
        ManagedMcpInspectionResult inspectionResult)
    {
        if (inspectionResult.ShouldRefreshManagedMcpServer)
        {
            Console.WriteLine(
                LocalizationService.GetString("ManagementExistingMcpServerRefreshing", "Existing managed LidGuard MCP server found for {0}. Refreshing registration.")
                    .Replace("{0}", ManagedProviderSelection.GetProviderDisplayName(provider), StringComparison.Ordinal));

            var removeExitCode = RemoveProviderMcp(provider);
            if (removeExitCode != 0)
            {
                Console.Error.WriteLine(
                    LocalizationService.GetString("ManagementSkippingMcpInstallAfterRemoveFailure", "Skipping {0} MCP install because removing the existing managed registration failed.")
                        .Replace("{0}", ManagedProviderSelection.GetProviderDisplayName(provider), StringComparison.Ordinal));
                return removeExitCode;
            }
        }

        return AddProviderMcp(provider, managedExecutableReference);
    }

    private static int AddProviderMcp(AgentProvider provider, string managedExecutableReference)
    {
        if (!ManagedProviderCliResolver.TryResolveProviderCliExecutablePath(provider, out var providerCliExecutablePath, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var processArguments = CreateProviderMcpInstallArguments(provider, managedExecutableReference);
        if (processArguments.Count == 0)
        {
            Console.Error.WriteLine(LocalizationService.GetString("ManagementUnsupportedMcpManagement"));
            return 1;
        }

        return ManagedProviderCliResolver.RunProviderProcess(providerCliExecutablePath, processArguments);
    }

    private static int RemoveProviderMcp(AgentProvider provider)
    {
        if (!ManagedProviderCliResolver.TryResolveProviderCliExecutablePath(provider, out var providerCliExecutablePath, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var processArguments = CreateProviderMcpRemoveArguments(provider);
        if (processArguments.Count == 0)
        {
            Console.Error.WriteLine(LocalizationService.GetString("ManagementUnsupportedMcpManagement"));
            return 1;
        }

        return ManagedProviderCliResolver.RunProviderProcess(providerCliExecutablePath, processArguments);
    }

    private static ManagedMcpInspectionResult InspectCodexMcp()
    {
        var configurationFilePath = CodexHookInstaller.GetDefaultCodexConfigurationFilePath();
        var configurationFileExists = File.Exists(configurationFilePath);
        ManagedProviderCliResolver.TryResolveProviderCliDisplayText(AgentProvider.Codex, out var hasProviderCli, out var providerCliDisplayText);
        var hasServerEntry = false;
        var matchesCurrentLidGuardExecutable = false;
        var containsExpectedServerCommand = false;
        var serverCommand = string.Empty;
        var serverArguments = LocalizationService.GetString("TextDisplayNone");
        var message = string.Empty;

        if (configurationFileExists)
        {
            var configurationContent = File.ReadAllText(configurationFilePath);
            if (McpConfigurationTomlUtilities.TryGetCodexMcpServerSectionContent(configurationContent, out var sectionContent))
            {
                hasServerEntry = true;
                McpConfigurationTomlUtilities.TryReadCodexMcpServerSection(sectionContent, out serverCommand, out var serverArgumentValues);
                serverArguments = McpConfigurationTomlUtilities.DescribeArgumentValues(serverArgumentValues);
                matchesCurrentLidGuardExecutable = HookCommandUtilities.ExecutableReferencesMatch(serverCommand, HookCommandUtilities.GetDefaultMcpExecutableReference());
                containsExpectedServerCommand = McpConfigurationTomlUtilities.ContainsArgument(serverArgumentValues, LidGuardMcpServerCommand.CommandName);
            }
            else
            {
                message = LocalizationService.GetFormattedString("ManagementNoMcpServerNamedFound", McpConfigurationTomlUtilities.ManagedMcpServerName);
            }
        }
        else
        {
            message = LocalizationService.GetFormattedString("ManagementConfigurationFileDoesNotExist", configurationFilePath);
        }

        return new ManagedMcpInspectionResult(
            AgentProvider.Codex,
            configurationFilePath,
            configurationFileExists,
            hasProviderCli,
            providerCliDisplayText,
            hasServerEntry,
            matchesCurrentLidGuardExecutable,
            containsExpectedServerCommand,
            string.Empty,
            serverCommand,
            serverArguments,
            string.Empty,
            ManagedMcpInspectionResult.GetStatusMessage(configurationFilePath, configurationFileExists, hasServerEntry, matchesCurrentLidGuardExecutable, containsExpectedServerCommand, message));
    }

    private static ManagedMcpInspectionResult InspectJsonProviderMcp(
        AgentProvider provider,
        string configurationFilePath)
    {
        var configurationFileExists = File.Exists(configurationFilePath);
        ManagedProviderCliResolver.TryResolveProviderCliDisplayText(provider, out var hasProviderCli, out var providerCliDisplayText);
        var hasServerEntry = false;
        var matchesCurrentLidGuardExecutable = false;
        var containsExpectedServerCommand = false;
        var serverType = string.Empty;
        var serverCommand = string.Empty;
        var serverArguments = LocalizationService.GetString("TextDisplayNone");
        var serverUrl = string.Empty;
        var message = string.Empty;

        if (configurationFileExists)
        {
            var configurationContent = File.ReadAllText(configurationFilePath);
            if (McpConfigurationJsonUtilities.TryGetJsonMcpServerEntry(configurationContent, McpConfigurationTomlUtilities.ManagedMcpServerName, out var serverObject, out message))
            {
                hasServerEntry = true;
                serverType = McpConfigurationJsonUtilities.GetJsonStringProperty(serverObject, "type");
                serverCommand = McpConfigurationJsonUtilities.GetJsonStringProperty(serverObject, "command");
                serverArguments = McpConfigurationJsonUtilities.DescribeJsonArray(serverObject, "args");
                serverUrl = McpConfigurationJsonUtilities.GetJsonStringProperty(serverObject, "url");
                matchesCurrentLidGuardExecutable = HookCommandUtilities.ExecutableReferencesMatch(serverCommand, HookCommandUtilities.GetDefaultMcpExecutableReference());
                containsExpectedServerCommand = McpConfigurationJsonUtilities.JsonArrayContainsStringValue(serverObject, "args", LidGuardMcpServerCommand.CommandName);
            }
        }

        return new ManagedMcpInspectionResult(
            provider,
            configurationFilePath,
            configurationFileExists,
            hasProviderCli,
            providerCliDisplayText,
            hasServerEntry,
            matchesCurrentLidGuardExecutable,
            containsExpectedServerCommand,
            serverType,
            serverCommand,
            serverArguments,
            serverUrl,
            ManagedMcpInspectionResult.GetStatusMessage(configurationFilePath, configurationFileExists, hasServerEntry, matchesCurrentLidGuardExecutable, containsExpectedServerCommand, message));
    }

    private static bool TryInspectProviderMcp(AgentProvider provider, out ManagedMcpInspectionResult inspectionResult)
    {
        inspectionResult = provider switch
        {
            AgentProvider.Codex => InspectCodexMcp(),
            AgentProvider.Claude => InspectJsonProviderMcp(AgentProvider.Claude, ManagedProviderConfigurationRoots.ClaudeUserConfigurationFilePath),
            AgentProvider.GitHubCopilot => InspectJsonProviderMcp(AgentProvider.GitHubCopilot, ManagedProviderConfigurationRoots.GitHubCopilotMcpConfigurationFilePath),
            _ => default
        };

        return provider is AgentProvider.Codex or AgentProvider.Claude or AgentProvider.GitHubCopilot;
    }

    private static int WriteUnsupportedProvider()
    {
        Console.Error.WriteLine(LocalizationService.GetString("ManagementUnsupportedMcpManagement"));
        return 1;
    }
}
