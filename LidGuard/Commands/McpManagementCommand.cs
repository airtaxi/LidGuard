using System.Text;
using LidGuard.Mcp;
using LidGuard.Sessions;
using LidGuard.Hooks;
using LidGuard.Localization;

namespace LidGuard.Commands;

internal static class McpManagementCommand
{
    private const string ManagedMcpServerName = "lidguard";

    private readonly record struct ManagedMcpInspectionResult(
        AgentProvider Provider,
        string ConfigurationFilePath,
        bool ConfigurationFileExists,
        bool HasProviderCli,
        string ProviderCliDisplayText,
        bool HasNamedServerEntry,
        bool MatchesManagedMcpServer,
        string ServerType,
        string ServerCommand,
        string ServerArguments,
        string ServerUrl,
        string Message)
    {
        public bool IsManagedMcpServerInstalled => HasNamedServerEntry && MatchesManagedMcpServer;
    }

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
                ? WriteProviderMcpStatus(inspectionResult)
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

    private static IReadOnlyList<string> CreateProviderMcpInstallArguments(AgentProvider provider, string managedExecutableReference)
    {
        return provider switch
        {
            AgentProvider.Codex => ["mcp", "add", ManagedMcpServerName, "--", managedExecutableReference, LidGuardMcpServerCommand.CommandName],
            AgentProvider.Claude => ["mcp", "add", "--scope", "user", ManagedMcpServerName, "--", managedExecutableReference, LidGuardMcpServerCommand.CommandName],
            AgentProvider.GitHubCopilot => ["mcp", "add", ManagedMcpServerName, "--", managedExecutableReference, LidGuardMcpServerCommand.CommandName],
            _ => []
        };
    }

    private static IReadOnlyList<string> CreateProviderMcpRemoveArguments(AgentProvider provider)
    {
        return provider switch
        {
            AgentProvider.Codex => ["mcp", "remove", ManagedMcpServerName],
            AgentProvider.Claude => ["mcp", "remove", "--scope", "user", ManagedMcpServerName],
            AgentProvider.GitHubCopilot => ["mcp", "remove", ManagedMcpServerName],
            _ => []
        };
    }

    private static string GetStatusMessage(
        string configurationFilePath,
        bool configurationFileExists,
        bool hasServerEntry,
        bool matchesManagedMcpServer,
        string parseMessage)
    {
        if (!configurationFileExists) return LocalizationService.GetFormattedString("ManagementConfigurationFileDoesNotExist", configurationFilePath);
        if (!string.IsNullOrWhiteSpace(parseMessage)) return parseMessage;
        if (!hasServerEntry) return LocalizationService.GetFormattedString("ManagementNoMcpServerNamedFound", ManagedMcpServerName);
        if (!matchesManagedMcpServer) return LocalizationService.GetString("ManagementMcpServerDoesNotPointAtManagedCommand", "The MCP server '{0}' exists but does not point at '{1}'.")
            .Replace("{0}", ManagedMcpServerName, StringComparison.Ordinal)
            .Replace("{1}", LidGuardMcpServerCommand.CommandName, StringComparison.Ordinal);
        return LocalizationService.GetString("ManagementLidGuardMcpServerRegistered", "LidGuard MCP server is registered.");
    }

    private static int InstallProviderMcp(
        AgentProvider provider,
        string managedExecutableReference,
        ManagedMcpInspectionResult inspectionResult)
    {
        if (inspectionResult.IsManagedMcpServerInstalled)
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
        var matchesManagedMcpServer = false;
        var message = string.Empty;

        if (configurationFileExists)
        {
            var configurationContent = File.ReadAllText(configurationFilePath);
            if (TryGetCodexMcpServerSectionContent(configurationContent, out var sectionContent))
            {
                hasServerEntry = true;
                matchesManagedMcpServer = sectionContent.Contains(LidGuardMcpServerCommand.CommandName, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                message = LocalizationService.GetFormattedString("ManagementNoMcpServerNamedFound", ManagedMcpServerName);
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
            matchesManagedMcpServer,
            string.Empty,
            string.Empty,
            LocalizationService.GetString("TextDisplayNone"),
            string.Empty,
            GetStatusMessage(configurationFilePath, configurationFileExists, hasServerEntry, matchesManagedMcpServer, message));
    }

    private static ManagedMcpInspectionResult InspectJsonProviderMcp(
        AgentProvider provider,
        string configurationFilePath)
    {
        var configurationFileExists = File.Exists(configurationFilePath);
        ManagedProviderCliResolver.TryResolveProviderCliDisplayText(provider, out var hasProviderCli, out var providerCliDisplayText);
        var hasServerEntry = false;
        var matchesManagedMcpServer = false;
        var serverType = string.Empty;
        var serverCommand = string.Empty;
        var serverArguments = LocalizationService.GetString("TextDisplayNone");
        var serverUrl = string.Empty;
        var message = string.Empty;

        if (configurationFileExists)
        {
            var configurationContent = File.ReadAllText(configurationFilePath);
            if (McpConfigurationJsonUtilities.TryGetJsonMcpServerEntry(configurationContent, ManagedMcpServerName, out var serverObject, out message))
            {
                hasServerEntry = true;
                serverType = McpConfigurationJsonUtilities.GetJsonStringProperty(serverObject, "type");
                serverCommand = McpConfigurationJsonUtilities.GetJsonStringProperty(serverObject, "command");
                serverArguments = McpConfigurationJsonUtilities.DescribeJsonArray(serverObject, "args");
                serverUrl = McpConfigurationJsonUtilities.GetJsonStringProperty(serverObject, "url");
                matchesManagedMcpServer =
                    serverCommand.Contains("lidguard", StringComparison.OrdinalIgnoreCase) &&
                    serverArguments.Contains(LidGuardMcpServerCommand.CommandName, StringComparison.OrdinalIgnoreCase);
            }
        }

        return new ManagedMcpInspectionResult(
            provider,
            configurationFilePath,
            configurationFileExists,
            hasProviderCli,
            providerCliDisplayText,
            hasServerEntry,
            matchesManagedMcpServer,
            serverType,
            serverCommand,
            serverArguments,
            serverUrl,
            GetStatusMessage(configurationFilePath, configurationFileExists, hasServerEntry, matchesManagedMcpServer, message));
    }

    private static bool TryGetCodexMcpServerSectionContent(string configurationContent, out string sectionContent)
    {
        sectionContent = string.Empty;
        var sectionHeader = $"[mcp_servers.{ManagedMcpServerName}]";
        var lineBuilder = new StringBuilder();
        var inTargetSection = false;
        foreach (var rawLine in configurationContent.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var trimmedLine = rawLine.Trim();
            if (trimmedLine.StartsWith("[", StringComparison.Ordinal) && trimmedLine.EndsWith("]", StringComparison.Ordinal))
            {
                if (inTargetSection) break;
                if (trimmedLine.Equals(sectionHeader, StringComparison.OrdinalIgnoreCase))
                {
                    inTargetSection = true;
                    continue;
                }
            }

            if (!inTargetSection) continue;
            lineBuilder.AppendLine(rawLine);
        }

        if (!inTargetSection) return false;

        sectionContent = lineBuilder.ToString();
        return true;
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

    private static int WriteProviderMcpStatus(ManagedMcpInspectionResult inspectionResult)
    {
        Console.WriteLine(LocalizationService.GetString("ManagementMcpInstallationTitle"));
        WriteField("ManagementLabelProvider", "Provider", inspectionResult.Provider);
        WriteField("ManagementLabelInstalled", "Installed", inspectionResult.HasNamedServerEntry);
        WriteField("ManagementLabelConfig", "Config", inspectionResult.ConfigurationFilePath);
        WriteField("ManagementLabelConfigExists", "Config exists", inspectionResult.ConfigurationFileExists);
        WriteField("ManagementLabelCliAvailable", "CLI available", inspectionResult.HasProviderCli);
        WriteField("ManagementLabelCli", "CLI", inspectionResult.ProviderCliDisplayText);
        WriteField("ManagementLabelServerName", "Server name", ManagedMcpServerName);
        WriteField("ManagementLabelManagedServerEntry", "Managed server entry", inspectionResult.HasNamedServerEntry);
        WriteField("ManagementLabelTransport", "Transport", inspectionResult.ServerType);
        WriteField("ManagementLabelCommand", "Command", inspectionResult.ServerCommand);
        WriteField("ManagementLabelArgs", "Args", inspectionResult.ServerArguments);
        WriteField("ManagementLabelUrl", "Url", inspectionResult.ServerUrl);
        WriteField("ManagementLabelContainsMcpServerCommand", "Contains mcp-server command", inspectionResult.MatchesManagedMcpServer);
        WriteField("ManagementLabelMessage", "Message", inspectionResult.Message);
        return 0;
    }

    private static int WriteUnsupportedProvider()
    {
        Console.Error.WriteLine(LocalizationService.GetString("ManagementUnsupportedMcpManagement"));
        return 1;
    }

    private static void WriteField(string labelResourceName, string fallbackLabel, object value)
    {
        var displayValue = value is bool booleanValue ? LocalizationService.DisplayBoolean(booleanValue) : LocalizationService.DisplayOptionalValue(value?.ToString() ?? string.Empty);
        Console.WriteLine(LocalizationService.GetFormattedString("ManagementField", LocalizationService.GetString(labelResourceName, fallbackLabel), displayValue));
    }
}
