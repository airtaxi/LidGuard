using System.Text;
using LidGuard.Mcp;
using LidGuardLib.Commons.Sessions;
using LidGuardLib.Hooks;

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

    public static int WriteMcpStatus(IReadOnlyDictionary<string, string> options)
    {
        if (!ManagedProviderSelection.TrySelectProviders(options, "Show MCP server status for provider", out var selectedProviders, out var message))
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
            if (providers.Count > 1) Console.WriteLine($"{ManagedProviderSelection.GetProviderDisplayName(provider)} MCP status:");

            var providerExitCode = TryInspectProviderMcp(provider, out var inspectionResult)
                ? WriteProviderMcpStatus(inspectionResult)
                : WriteUnsupportedProvider();

            if (providerExitCode != 0) exitCode = providerExitCode;
            if (providers.Count > 1) Console.WriteLine();
        }

        return exitCode;
    }

    public static int InstallMcp(IReadOnlyDictionary<string, string> options)
    {
        if (!ManagedProviderSelection.TrySelectProviders(options, "Install MCP server for provider", out var selectedProviders, out var message))
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
            Console.Error.WriteLine($"LidGuard executable or command does not exist: {managedExecutableReference}");
            return 1;
        }

        var exitCode = 0;
        foreach (var provider in providers)
        {
            if (providers.Count > 1) Console.WriteLine($"Installing {ManagedProviderSelection.GetProviderDisplayName(provider)} MCP server...");
            var providerExitCode = TryInspectProviderMcp(provider, out var inspectionResult)
                ? InstallProviderMcp(provider, managedExecutableReference, inspectionResult)
                : WriteUnsupportedProvider();
            if (providerExitCode != 0) exitCode = providerExitCode;
            if (providers.Count > 1) Console.WriteLine();
        }

        return exitCode;
    }

    public static int RemoveMcp(IReadOnlyDictionary<string, string> options)
    {
        if (!ManagedProviderSelection.TrySelectProviders(options, "Remove MCP server for provider", out var selectedProviders, out var message))
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
            if (providers.Count > 1) Console.WriteLine($"Removing {ManagedProviderSelection.GetProviderDisplayName(provider)} MCP server...");
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
        if (!configurationFileExists) return $"Configuration file does not exist: {configurationFilePath}";
        if (!string.IsNullOrWhiteSpace(parseMessage)) return parseMessage;
        if (!hasServerEntry) return $"No MCP server named '{ManagedMcpServerName}' was found.";
        if (!matchesManagedMcpServer) return $"The MCP server '{ManagedMcpServerName}' exists but does not point at '{LidGuardMcpServerCommand.CommandName}'.";
        return "LidGuard MCP server is registered.";
    }

    private static int InstallProviderMcp(
        AgentProvider provider,
        string managedExecutableReference,
        ManagedMcpInspectionResult inspectionResult)
    {
        if (inspectionResult.IsManagedMcpServerInstalled)
        {
            Console.WriteLine(
                $"Existing managed LidGuard MCP server found for {ManagedProviderSelection.GetProviderDisplayName(provider)}. Refreshing registration...");

            var removeExitCode = RemoveProviderMcp(provider);
            if (removeExitCode != 0)
            {
                Console.Error.WriteLine(
                    $"Skipping {ManagedProviderSelection.GetProviderDisplayName(provider)} MCP install because removing the existing managed registration failed.");
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
            Console.Error.WriteLine("Only Codex, Claude, and GitHub Copilot MCP management are implemented.");
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
            Console.Error.WriteLine("Only Codex, Claude, and GitHub Copilot MCP management are implemented.");
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
                message = $"No MCP server named '{ManagedMcpServerName}' was found.";
            }
        }
        else
        {
            message = $"Configuration file does not exist: {configurationFilePath}";
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
            "<none>",
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
        var serverArguments = "<none>";
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
        Console.WriteLine("MCP installation:");
        Console.WriteLine($"  Provider: {inspectionResult.Provider}");
        Console.WriteLine($"  Installed: {inspectionResult.HasNamedServerEntry}");
        Console.WriteLine($"  Config: {inspectionResult.ConfigurationFilePath}");
        Console.WriteLine($"  Config exists: {inspectionResult.ConfigurationFileExists}");
        Console.WriteLine($"  CLI available: {inspectionResult.HasProviderCli}");
        Console.WriteLine($"  CLI: {inspectionResult.ProviderCliDisplayText}");
        Console.WriteLine($"  Server name: {ManagedMcpServerName}");
        Console.WriteLine($"  Managed server entry: {inspectionResult.HasNamedServerEntry}");
        Console.WriteLine($"  Transport: {(string.IsNullOrWhiteSpace(inspectionResult.ServerType) ? "<none>" : inspectionResult.ServerType)}");
        Console.WriteLine($"  Command: {(string.IsNullOrWhiteSpace(inspectionResult.ServerCommand) ? "<none>" : inspectionResult.ServerCommand)}");
        Console.WriteLine($"  Args: {inspectionResult.ServerArguments}");
        Console.WriteLine($"  Url: {(string.IsNullOrWhiteSpace(inspectionResult.ServerUrl) ? "<none>" : inspectionResult.ServerUrl)}");
        Console.WriteLine($"  Contains mcp-server command: {inspectionResult.MatchesManagedMcpServer}");
        Console.WriteLine($"  Message: {inspectionResult.Message}");
        return 0;
    }

    private static int WriteUnsupportedProvider()
    {
        Console.Error.WriteLine("Only Codex, Claude, and GitHub Copilot MCP management are implemented.");
        return 1;
    }
}
