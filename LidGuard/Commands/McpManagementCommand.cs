using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LidGuard.Mcp;
using LidGuardLib.Commons.Sessions;
using LidGuardLib.Windows.Hooks;

namespace LidGuard.Commands;

internal static class McpManagementCommand
{
    private const string ManagedMcpServerName = "lidguard";
    private const string ClaudeUserConfigurationFileName = ".claude.json";
    private const string CopilotMcpConfigurationFileName = "mcp-config.json";

    public static int WriteMcpStatus(IReadOnlyDictionary<string, string> options)
    {
        if (!ManagedProviderSelection.TrySelectProviders(options, "Show MCP server status for provider", out var selectedProviders, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        ManagedProviderSelection.ResolveAvailableProviders(
            selectedProviders,
            GetProviderConfigurationRootCandidatePaths,
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
            GetProviderConfigurationRootCandidatePaths,
            out var providers,
            out var skippedProviderMessages);

        ManagedProviderSelection.WriteSkippedProviderMessages(skippedProviderMessages);
        if (providers.Count == 0) return ManagedProviderSelection.WriteNoAvailableProvidersFound();

        var managedExecutableReference = WindowsHookCommandUtilities.GetDefaultMcpExecutableReference();
        if (!WindowsHookCommandUtilities.HookExecutableExists(managedExecutableReference))
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
            GetProviderConfigurationRootCandidatePaths,
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

    private static string DescribeJsonArray(JsonObject jsonObject, string propertyName)
    {
        if (jsonObject[propertyName] is not JsonArray jsonArray || jsonArray.Count == 0) return "<none>";

        var values = new List<string>();
        foreach (var item in jsonArray)
        {
            if (item is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var stringValue))
            {
                values.Add(stringValue);
            }
            else
            {
                values.Add(item?.ToJsonString() ?? "null");
            }
        }

        return string.Join(" | ", values);
    }

    private static IReadOnlyList<string> GetProviderCliCandidatePaths(AgentProvider provider)
    {
        var localApplicationDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var wingetLinksDirectoryPath = Path.Combine(localApplicationDataPath, "Microsoft", "WinGet", "Links");

        return provider switch
        {
            AgentProvider.Codex =>
            [
                "codex",
                Path.Combine(wingetLinksDirectoryPath, "codex.exe"),
                Path.Combine(localApplicationDataPath, "Programs", "OpenAI", "Codex", "bin", "codex.exe")
            ],
            AgentProvider.Claude =>
            [
                "claude",
                Path.Combine(wingetLinksDirectoryPath, "claude.exe")
            ],
            AgentProvider.GitHubCopilot =>
            [
                "copilot",
                Path.Combine(wingetLinksDirectoryPath, "copilot.exe")
            ],
            _ => []
        };
    }

    private static string GetJsonStringProperty(JsonObject jsonObject, string propertyName)
    {
        var valueNode = jsonObject[propertyName];
        return valueNode is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var value) ? value : string.Empty;
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

    private static string GetCodexMcpConfigurationFilePath() => WindowsCodexHookInstaller.GetDefaultCodexConfigurationFilePath();

    private static IReadOnlyList<string> GetProviderConfigurationRootCandidatePaths(AgentProvider provider)
    {
        var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return provider switch
        {
            AgentProvider.Codex =>
            [
                WindowsCodexHookInstaller.GetDefaultCodexConfigurationDirectoryPath(),
                WindowsCodexHookInstaller.GetDefaultCodexConfigurationFilePath()
            ],
            AgentProvider.Claude =>
            [
                Path.Combine(userProfilePath, ClaudeUserConfigurationFileName),
                WindowsClaudeHookInstaller.GetDefaultClaudeConfigurationDirectoryPath()
            ],
            AgentProvider.GitHubCopilot =>
            [
                Path.Combine(WindowsGitHubCopilotHookInstaller.GetDefaultGitHubCopilotConfigurationDirectoryPath(), CopilotMcpConfigurationFileName),
                WindowsGitHubCopilotHookInstaller.GetDefaultGitHubCopilotConfigurationDirectoryPath()
            ],
            _ => []
        };
    }

    private static string GetProviderCliDisplayText(AgentProvider provider, bool hasProviderCli, string providerCliExecutablePath)
    {
        if (hasProviderCli && !string.IsNullOrWhiteSpace(providerCliExecutablePath)) return providerCliExecutablePath;
        return string.Join(" | ", GetProviderCliCandidatePaths(provider));
    }

    private static string GetUserProfileFilePath(string fileName)
    {
        var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfilePath, fileName);
    }

    private static int InstallProviderMcp(AgentProvider provider, string managedExecutableReference)
    {
        if (!TryResolveProviderCliExecutablePath(provider, out var providerCliExecutablePath, out var message))
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

        return RunProviderProcess(providerCliExecutablePath, processArguments);
    }

    private static int RemoveProviderMcp(AgentProvider provider)
    {
        if (!TryResolveProviderCliExecutablePath(provider, out var providerCliExecutablePath, out var message))
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

        return RunProviderProcess(providerCliExecutablePath, processArguments);
    }

    private static int RunProviderProcess(string fileName, IReadOnlyList<string> arguments)
    {
        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                WorkingDirectory = Environment.CurrentDirectory
            };

            foreach (var argument in arguments) processStartInfo.ArgumentList.Add(argument);

            using var process = new Process { StartInfo = processStartInfo };
            if (!process.Start())
            {
                Console.Error.WriteLine($"Failed to start process: {fileName}");
                return 1;
            }

            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
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

    private static bool TryGetJsonMcpServerEntry(string configurationContent, out JsonObject serverObject, out string message)
    {
        serverObject = new JsonObject();
        message = string.Empty;

        JsonObject rootObject;
        try
        {
            var rootNode = JsonNode.Parse(configurationContent, documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            if (rootNode is not JsonObject existingRootObject)
            {
                message = "Configuration root is not a JSON object.";
                return false;
            }

            rootObject = existingRootObject;
        }
        catch (JsonException exception)
        {
            message = $"Configuration JSON is invalid: {exception.Message}";
            return false;
        }

        if (rootObject["mcpServers"] is not JsonObject mcpServersObject)
        {
            message = "The mcpServers object was not found.";
            return false;
        }

        if (mcpServersObject[ManagedMcpServerName] is not JsonObject existingServerObject)
        {
            message = $"No MCP server named '{ManagedMcpServerName}' was found.";
            return false;
        }

        serverObject = existingServerObject;
        return true;
    }

    private static bool TryResolveProviderCliDisplayText(AgentProvider provider, out bool hasProviderCli, out string providerCliDisplayText)
    {
        hasProviderCli = TryResolveProviderCliExecutablePath(provider, out var providerCliExecutablePath, out _);
        providerCliDisplayText = GetProviderCliDisplayText(provider, hasProviderCli, providerCliExecutablePath);
        return hasProviderCli;
    }

    private static bool TryResolveProviderCliExecutablePath(AgentProvider provider, out string providerCliExecutablePath, out string message)
    {
        providerCliExecutablePath = string.Empty;
        message = string.Empty;

        foreach (var candidatePath in GetProviderCliCandidatePaths(provider))
        {
            if (!WindowsHookCommandUtilities.HookExecutableExists(candidatePath)) continue;

            providerCliExecutablePath = WindowsHookCommandUtilities.NormalizeHookExecutableReference(candidatePath);
            return true;
        }

        message =
            $"Provider CLI not found: {ManagedProviderSelection.GetProviderDisplayName(provider)} (checked: {string.Join(" | ", GetProviderCliCandidatePaths(provider))})";
        return false;
    }

    private static int WriteClaudeMcpStatus()
    {
        var configurationFilePath = GetUserProfileFilePath(ClaudeUserConfigurationFileName);
        var configurationFileExists = File.Exists(configurationFilePath);
        TryResolveProviderCliDisplayText(AgentProvider.Claude, out var hasProviderCli, out var providerCliDisplayText);
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
            if (TryGetJsonMcpServerEntry(configurationContent, out var serverObject, out message))
            {
                hasServerEntry = true;
                serverType = GetJsonStringProperty(serverObject, "type");
                serverCommand = GetJsonStringProperty(serverObject, "command");
                serverArguments = DescribeJsonArray(serverObject, "args");
                serverUrl = GetJsonStringProperty(serverObject, "url");
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
        var configurationFilePath = GetCodexMcpConfigurationFilePath();
        var configurationFileExists = File.Exists(configurationFilePath);
        TryResolveProviderCliDisplayText(AgentProvider.Codex, out var hasProviderCli, out var providerCliDisplayText);
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
        var configurationFilePath = Path.Combine(
            WindowsGitHubCopilotHookInstaller.GetDefaultGitHubCopilotConfigurationDirectoryPath(),
            CopilotMcpConfigurationFileName);
        var configurationFileExists = File.Exists(configurationFilePath);
        TryResolveProviderCliDisplayText(AgentProvider.GitHubCopilot, out var hasProviderCli, out var providerCliDisplayText);
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
            if (TryGetJsonMcpServerEntry(configurationContent, out var serverObject, out message))
            {
                hasServerEntry = true;
                serverType = GetJsonStringProperty(serverObject, "type");
                serverCommand = GetJsonStringProperty(serverObject, "command");
                serverArguments = DescribeJsonArray(serverObject, "args");
                serverUrl = GetJsonStringProperty(serverObject, "url");
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
