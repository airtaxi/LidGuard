using System.Text;
using LidGuard.Mcp;
using LidGuardLib.Commons.Sessions;
using LidGuardLib.Hooks;

namespace LidGuard.Commands;

internal static class McpManagementCommand
{
    private const string ManagedMcpServerName = "lidguard";

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

            var providerExitCode = provider switch
            {
                AgentProvider.Codex => WriteCodexMcpStatus(),
                AgentProvider.Claude => WriteClaudeMcpStatus(),
                AgentProvider.GitHubCopilot => WriteGitHubCopilotMcpStatus(),
                _ => WriteUnsupportedProvider()
            };

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
            var providerExitCode = InstallProviderMcp(provider, managedExecutableReference);
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

    private static string GetJsonStatusMessage(
        string configurationFilePath,
        bool configurationFileExists,
        bool hasServerEntry,
        bool containsMcpServerCommand,
        string parseMessage)
    {
        if (!configurationFileExists) return $"Configuration file does not exist: {configurationFilePath}";
        if (!string.IsNullOrWhiteSpace(parseMessage)) return parseMessage;
        if (!hasServerEntry) return $"No MCP server named '{ManagedMcpServerName}' was found.";
        if (!containsMcpServerCommand) return $"The MCP server '{ManagedMcpServerName}' exists but does not point at '{LidGuardMcpServerCommand.CommandName}'.";
        return "LidGuard MCP server is registered.";
    }

    private static int InstallProviderMcp(AgentProvider provider, string managedExecutableReference)
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

    private static int WriteClaudeMcpStatus()
    {
        var configurationFilePath = ManagedProviderConfigurationRoots.ClaudeUserConfigurationFilePath;
        var configurationFileExists = File.Exists(configurationFilePath);
        ManagedProviderCliResolver.TryResolveProviderCliDisplayText(AgentProvider.Claude, out var hasProviderCli, out var providerCliDisplayText);
        var hasServerEntry = false;
        var containsMcpServerCommand = false;
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
                containsMcpServerCommand =
                    serverCommand.Contains("lidguard", StringComparison.OrdinalIgnoreCase) &&
                    serverArguments.Contains(LidGuardMcpServerCommand.CommandName, StringComparison.OrdinalIgnoreCase);
            }
        }

        WriteJsonMcpStatus(
            AgentProvider.Claude,
            configurationFilePath,
            configurationFileExists,
            hasProviderCli,
            providerCliDisplayText,
            hasServerEntry,
            containsMcpServerCommand,
            serverType,
            serverCommand,
            serverArguments,
            serverUrl,
            GetJsonStatusMessage(configurationFilePath, configurationFileExists, hasServerEntry, containsMcpServerCommand, message));
        return 0;
    }

    private static int WriteCodexMcpStatus()
    {
        var configurationFilePath = CodexHookInstaller.GetDefaultCodexConfigurationFilePath();
        var configurationFileExists = File.Exists(configurationFilePath);
        ManagedProviderCliResolver.TryResolveProviderCliDisplayText(AgentProvider.Codex, out var hasProviderCli, out var providerCliDisplayText);
        var hasServerEntry = false;
        var containsMcpServerCommand = false;
        var message = string.Empty;

        if (configurationFileExists)
        {
            var configurationContent = File.ReadAllText(configurationFilePath);
            if (TryGetCodexMcpServerSectionContent(configurationContent, out var sectionContent))
            {
                hasServerEntry = true;
                containsMcpServerCommand = sectionContent.Contains(LidGuardMcpServerCommand.CommandName, StringComparison.OrdinalIgnoreCase);
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

        Console.WriteLine("MCP installation:");
        Console.WriteLine($"  Provider: {AgentProvider.Codex}");
        Console.WriteLine($"  Installed: {hasServerEntry}");
        Console.WriteLine($"  Config: {configurationFilePath}");
        Console.WriteLine($"  Config exists: {configurationFileExists}");
        Console.WriteLine($"  CLI available: {hasProviderCli}");
        Console.WriteLine($"  CLI: {providerCliDisplayText}");
        Console.WriteLine($"  Server name: {ManagedMcpServerName}");
        Console.WriteLine($"  Managed server entry: {hasServerEntry}");
        Console.WriteLine($"  Contains mcp-server command: {containsMcpServerCommand}");
        Console.WriteLine($"  Message: {GetJsonStatusMessage(configurationFilePath, configurationFileExists, hasServerEntry, containsMcpServerCommand, message)}");
        return 0;
    }

    private static int WriteGitHubCopilotMcpStatus()
    {
        var configurationFilePath = ManagedProviderConfigurationRoots.GitHubCopilotMcpConfigurationFilePath;
        var configurationFileExists = File.Exists(configurationFilePath);
        ManagedProviderCliResolver.TryResolveProviderCliDisplayText(AgentProvider.GitHubCopilot, out var hasProviderCli, out var providerCliDisplayText);
        var hasServerEntry = false;
        var containsMcpServerCommand = false;
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
                containsMcpServerCommand =
                    serverCommand.Contains("lidguard", StringComparison.OrdinalIgnoreCase) &&
                    serverArguments.Contains(LidGuardMcpServerCommand.CommandName, StringComparison.OrdinalIgnoreCase);
            }
        }

        WriteJsonMcpStatus(
            AgentProvider.GitHubCopilot,
            configurationFilePath,
            configurationFileExists,
            hasProviderCli,
            providerCliDisplayText,
            hasServerEntry,
            containsMcpServerCommand,
            serverType,
            serverCommand,
            serverArguments,
            serverUrl,
            GetJsonStatusMessage(configurationFilePath, configurationFileExists, hasServerEntry, containsMcpServerCommand, message));
        return 0;
    }

    private static void WriteJsonMcpStatus(
        AgentProvider provider,
        string configurationFilePath,
        bool configurationFileExists,
        bool hasProviderCli,
        string providerCliDisplayText,
        bool hasServerEntry,
        bool containsMcpServerCommand,
        string serverType,
        string serverCommand,
        string serverArguments,
        string serverUrl,
        string message)
    {
        Console.WriteLine("MCP installation:");
        Console.WriteLine($"  Provider: {provider}");
        Console.WriteLine($"  Installed: {hasServerEntry}");
        Console.WriteLine($"  Config: {configurationFilePath}");
        Console.WriteLine($"  Config exists: {configurationFileExists}");
        Console.WriteLine($"  CLI available: {hasProviderCli}");
        Console.WriteLine($"  CLI: {providerCliDisplayText}");
        Console.WriteLine($"  Server name: {ManagedMcpServerName}");
        Console.WriteLine($"  Managed server entry: {hasServerEntry}");
        Console.WriteLine($"  Transport: {(string.IsNullOrWhiteSpace(serverType) ? "<none>" : serverType)}");
        Console.WriteLine($"  Command: {(string.IsNullOrWhiteSpace(serverCommand) ? "<none>" : serverCommand)}");
        Console.WriteLine($"  Args: {serverArguments}");
        Console.WriteLine($"  Url: {(string.IsNullOrWhiteSpace(serverUrl) ? "<none>" : serverUrl)}");
        Console.WriteLine($"  Contains mcp-server command: {containsMcpServerCommand}");
        Console.WriteLine($"  Message: {message}");
    }

    private static int WriteUnsupportedProvider()
    {
        Console.Error.WriteLine("Only Codex, Claude, and GitHub Copilot MCP management are implemented.");
        return 1;
    }
}
