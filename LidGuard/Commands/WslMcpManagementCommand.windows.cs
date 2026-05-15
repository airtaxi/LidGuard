using System.Text;
using LidGuard.Hooks;
using LidGuard.Ipc;
using LidGuard.Localization;
using LidGuard.Mcp;
using LidGuard.Sessions;

namespace LidGuard.Commands;

internal static class WslMcpManagementCommand
{
    private const string ManagedMcpServerName = "lidguard";

    private readonly record struct WslManagedMcpInspectionResult(
        AgentProvider Provider,
        string ConfigurationFilePath,
        bool ConfigurationFileExists,
        bool HasProviderCli,
        string ProviderCliDisplayText,
        bool HasNamedServerEntry,
        bool MatchesCurrentLidGuardExecutable,
        bool ContainsExpectedServerCommand,
        string ServerType,
        string ServerCommand,
        string ServerArguments,
        string ServerUrl,
        string Message)
    {
        public bool IsManagedMcpServerInstalled => HasNamedServerEntry && MatchesCurrentLidGuardExecutable && ContainsExpectedServerCommand;

        public bool ShouldRefreshManagedMcpServer => HasNamedServerEntry && ContainsExpectedServerCommand;
    }

    public static bool IsCommandName(string commandName)
        => (commandName is LidGuardPipeCommands.WslMcpStatus
            or LidGuardPipeCommands.WslMcpInstall
            or LidGuardPipeCommands.WslMcpRemove
            or "wsl-mcp-uninstall")
            || TryGetAliasProvider(commandName, out _, out _);

    public static int Run(string commandName, string[] commandLineArguments)
    {
        if (!TryParseArguments(commandName, commandLineArguments, out var operationName, out var providerText, out var options, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var distroName = WslCommandUtilities.GetDistroName(options);
        if (!WslCommandUtilities.TryValidateWsl(distroName, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        if (!WslCommandUtilities.TryGetWslLidGuardExecutablePath(distroName, out var wslExecutablePath, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        return operationName switch
        {
            LidGuardPipeCommands.WslMcpStatus => WriteMcpStatus(providerText, distroName, wslExecutablePath),
            LidGuardPipeCommands.WslMcpInstall => InstallMcp(providerText, distroName, wslExecutablePath),
            LidGuardPipeCommands.WslMcpRemove => RemoveMcp(providerText, distroName, wslExecutablePath),
            _ => LidGuardCommandConsole.WriteUnknownCommand(commandName)
        };
    }

    private static int WriteMcpStatus(string providerText, string distroName, string wslExecutablePath)
    {
        if (!TrySelectMcpProviders(providerText, LocalizationService.GetString("ManagementPromptMcpStatus", "Show MCP server status for provider"), distroName, out var providers, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var exitCode = 0;
        foreach (var provider in providers)
        {
            if (providers.Count > 1) Console.WriteLine(LocalizationService.GetFormattedString("ManagementMcpStatusTitle", ManagedProviderSelection.GetProviderDisplayName(provider)));

            var providerExitCode = TryInspectProviderMcp(provider, distroName, wslExecutablePath, out var inspectionResult)
                ? WriteProviderMcpStatus(inspectionResult)
                : WriteUnsupportedProvider();

            if (providerExitCode != 0) exitCode = providerExitCode;
            if (providers.Count > 1) Console.WriteLine();
        }

        return exitCode;
    }

    private static int InstallMcp(string providerText, string distroName, string wslExecutablePath)
    {
        if (!TrySelectMcpProviders(providerText, LocalizationService.GetString("ManagementPromptMcpInstall", "Install MCP server for provider"), distroName, out var providers, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var exitCode = 0;
        foreach (var provider in providers)
        {
            if (providers.Count > 1) Console.WriteLine(LocalizationService.GetFormattedString("ManagementInstallingMcpServer", ManagedProviderSelection.GetProviderDisplayName(provider)));
            var providerExitCode = TryInspectProviderMcp(provider, distroName, wslExecutablePath, out var inspectionResult)
                ? InstallProviderMcp(provider, distroName, wslExecutablePath, inspectionResult)
                : WriteUnsupportedProvider();
            if (providerExitCode != 0) exitCode = providerExitCode;
            if (providers.Count > 1) Console.WriteLine();
        }

        return exitCode;
    }

    private static int RemoveMcp(string providerText, string distroName, string wslExecutablePath)
    {
        if (!TrySelectMcpProviders(providerText, LocalizationService.GetString("ManagementPromptMcpRemove", "Remove MCP server for provider"), distroName, out var providers, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var exitCode = 0;
        foreach (var provider in providers)
        {
            if (providers.Count > 1) Console.WriteLine(LocalizationService.GetFormattedString("ManagementRemovingMcpServer", ManagedProviderSelection.GetProviderDisplayName(provider)));
            var providerExitCode = RemoveProviderMcp(provider, distroName);
            if (providerExitCode != 0) exitCode = providerExitCode;
            if (providers.Count > 1) Console.WriteLine();
        }

        return exitCode;
    }

    private static int InstallProviderMcp(
        AgentProvider provider,
        string distroName,
        string wslExecutablePath,
        WslManagedMcpInspectionResult inspectionResult)
    {
        if (inspectionResult.ShouldRefreshManagedMcpServer)
        {
            Console.WriteLine(
                LocalizationService.GetString("ManagementExistingMcpServerRefreshing", "Existing managed LidGuard MCP server found for {0}. Refreshing registration.")
                    .Replace("{0}", ManagedProviderSelection.GetProviderDisplayName(provider), StringComparison.Ordinal));

            var removeExitCode = RemoveProviderMcp(provider, distroName);
            if (removeExitCode != 0)
            {
                Console.Error.WriteLine(
                    LocalizationService.GetString("ManagementSkippingMcpInstallAfterRemoveFailure", "Skipping {0} MCP install because removing the existing managed registration failed.")
                        .Replace("{0}", ManagedProviderSelection.GetProviderDisplayName(provider), StringComparison.Ordinal));
                return removeExitCode;
            }
        }

        return AddProviderMcp(provider, distroName, wslExecutablePath);
    }

    private static int AddProviderMcp(AgentProvider provider, string distroName, string wslExecutablePath)
    {
        if (!TryResolveProviderCli(provider, distroName, out var providerCliExecutableName)) return 1;

        var processArguments = CreateProviderMcpInstallArguments(provider, wslExecutablePath);
        if (processArguments.Count == 0) return WriteUnsupportedProvider();

        return RunProviderProcess(distroName, providerCliExecutableName, processArguments);
    }

    private static int RemoveProviderMcp(AgentProvider provider, string distroName)
    {
        if (!TryResolveProviderCli(provider, distroName, out var providerCliExecutableName)) return 1;

        var processArguments = CreateProviderMcpRemoveArguments(provider);
        if (processArguments.Count == 0) return WriteUnsupportedProvider();

        return RunProviderProcess(distroName, providerCliExecutableName, processArguments);
    }

    private static IReadOnlyList<string> CreateProviderMcpInstallArguments(AgentProvider provider, string wslExecutablePath)
    {
        return provider switch
        {
            AgentProvider.Codex => ["mcp", "add", ManagedMcpServerName, "--", wslExecutablePath, LidGuardMcpServerCommand.CommandName],
            AgentProvider.Claude => ["mcp", "add", "--scope", "user", ManagedMcpServerName, "--", wslExecutablePath, LidGuardMcpServerCommand.CommandName],
            AgentProvider.GitHubCopilot => ["mcp", "add", ManagedMcpServerName, "--", wslExecutablePath, LidGuardMcpServerCommand.CommandName],
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

    private static WslManagedMcpInspectionResult InspectCodexMcp(string distroName, string wslExecutablePath)
    {
        if (!WslProviderConfigurationRoots.TryGetMcpConfigurationFilePath(distroName, AgentProvider.Codex, out var configurationFilePath, out var message))
        {
            configurationFilePath = string.Empty;
        }

        var configurationFileExists = WslCommandUtilities.FileExists(distroName, configurationFilePath);
        WslCommandUtilities.TryResolveProviderCliDisplayText(distroName, AgentProvider.Codex, out var hasProviderCli, out var providerCliDisplayText);
        var hasServerEntry = false;
        var matchesCurrentLidGuardExecutable = false;
        var containsExpectedServerCommand = false;
        var serverCommand = string.Empty;
        var serverArguments = LocalizationService.GetString("TextDisplayNone");

        if (configurationFileExists)
        {
            if (!WslCommandUtilities.TryReadTextFile(distroName, configurationFilePath, out var configurationContent, out message))
            {
                message = LocalizationService.GetString("McpConfigurationJsonInvalid", "Configuration JSON is invalid: {0}")
                    .Replace("{0}", message, StringComparison.Ordinal);
            }
            else if (TryGetCodexMcpServerSectionContent(configurationContent, out var sectionContent))
            {
                hasServerEntry = true;
                TryReadCodexMcpServerSection(sectionContent, out serverCommand, out var serverArgumentValues);
                serverArguments = DescribeArgumentValues(serverArgumentValues);
                matchesCurrentLidGuardExecutable = WslCommandUtilities.ExecutableReferencesMatch(serverCommand, wslExecutablePath);
                containsExpectedServerCommand = ContainsArgument(serverArgumentValues, LidGuardMcpServerCommand.CommandName);
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

        return new WslManagedMcpInspectionResult(
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
            GetStatusMessage(configurationFilePath, configurationFileExists, hasServerEntry, matchesCurrentLidGuardExecutable, containsExpectedServerCommand, message));
    }

    private static WslManagedMcpInspectionResult InspectJsonProviderMcp(
        AgentProvider provider,
        string distroName,
        string wslExecutablePath)
    {
        if (!WslProviderConfigurationRoots.TryGetMcpConfigurationFilePath(distroName, provider, out var configurationFilePath, out var message))
        {
            configurationFilePath = string.Empty;
        }

        var configurationFileExists = WslCommandUtilities.FileExists(distroName, configurationFilePath);
        WslCommandUtilities.TryResolveProviderCliDisplayText(distroName, provider, out var hasProviderCli, out var providerCliDisplayText);
        var hasServerEntry = false;
        var matchesCurrentLidGuardExecutable = false;
        var containsExpectedServerCommand = false;
        var serverType = string.Empty;
        var serverCommand = string.Empty;
        var serverArguments = LocalizationService.GetString("TextDisplayNone");
        var serverUrl = string.Empty;

        if (configurationFileExists)
        {
            if (!WslCommandUtilities.TryReadTextFile(distroName, configurationFilePath, out var configurationContent, out message))
            {
                message = LocalizationService.GetString("McpConfigurationJsonInvalid", "Configuration JSON is invalid: {0}")
                    .Replace("{0}", message, StringComparison.Ordinal);
            }
            else if (McpConfigurationJsonUtilities.TryGetJsonMcpServerEntry(configurationContent, ManagedMcpServerName, out var serverObject, out message))
            {
                hasServerEntry = true;
                serverType = McpConfigurationJsonUtilities.GetJsonStringProperty(serverObject, "type");
                serverCommand = McpConfigurationJsonUtilities.GetJsonStringProperty(serverObject, "command");
                serverArguments = McpConfigurationJsonUtilities.DescribeJsonArray(serverObject, "args");
                serverUrl = McpConfigurationJsonUtilities.GetJsonStringProperty(serverObject, "url");
                matchesCurrentLidGuardExecutable = WslCommandUtilities.ExecutableReferencesMatch(serverCommand, wslExecutablePath);
                containsExpectedServerCommand = McpConfigurationJsonUtilities.JsonArrayContainsStringValue(serverObject, "args", LidGuardMcpServerCommand.CommandName);
            }
        }

        return new WslManagedMcpInspectionResult(
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
            GetStatusMessage(configurationFilePath, configurationFileExists, hasServerEntry, matchesCurrentLidGuardExecutable, containsExpectedServerCommand, message));
    }

    private static bool TryInspectProviderMcp(
        AgentProvider provider,
        string distroName,
        string wslExecutablePath,
        out WslManagedMcpInspectionResult inspectionResult)
    {
        inspectionResult = provider switch
        {
            AgentProvider.Codex => InspectCodexMcp(distroName, wslExecutablePath),
            AgentProvider.Claude => InspectJsonProviderMcp(AgentProvider.Claude, distroName, wslExecutablePath),
            AgentProvider.GitHubCopilot => InspectJsonProviderMcp(AgentProvider.GitHubCopilot, distroName, wslExecutablePath),
            _ => default
        };

        return provider is AgentProvider.Codex or AgentProvider.Claude or AgentProvider.GitHubCopilot;
    }

    private static int WriteProviderMcpStatus(WslManagedMcpInspectionResult inspectionResult)
    {
        Console.WriteLine(LocalizationService.GetString("ManagementMcpInstallationTitle"));
        WriteField("ManagementLabelProvider", "Provider", inspectionResult.Provider);
        WriteField("ManagementLabelInstalled", "Installed", inspectionResult.IsManagedMcpServerInstalled);
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
        WriteField("ManagementLabelMatchesCurrentLidGuardExecutable", "Matches current LidGuard executable", inspectionResult.MatchesCurrentLidGuardExecutable);
        WriteField("ManagementLabelContainsMcpServerCommand", "Contains mcp-server command", inspectionResult.ContainsExpectedServerCommand);
        WriteField("ManagementLabelMessage", "Message", inspectionResult.Message);
        return 0;
    }

    private static string GetStatusMessage(
        string configurationFilePath,
        bool configurationFileExists,
        bool hasServerEntry,
        bool matchesCurrentLidGuardExecutable,
        bool containsExpectedServerCommand,
        string parseMessage)
    {
        if (!configurationFileExists) return LocalizationService.GetFormattedString("ManagementConfigurationFileDoesNotExist", configurationFilePath);
        if (!string.IsNullOrWhiteSpace(parseMessage)) return parseMessage;
        if (!hasServerEntry) return LocalizationService.GetFormattedString("ManagementNoMcpServerNamedFound", ManagedMcpServerName);
        if (!matchesCurrentLidGuardExecutable) return LocalizationService.GetString("ManagementMcpServerDoesNotPointAtCurrentExecutable", "The MCP server '{0}' exists but does not point at the current LidGuard executable.")
            .Replace("{0}", ManagedMcpServerName, StringComparison.Ordinal);
        if (!containsExpectedServerCommand) return LocalizationService.GetString("ManagementMcpServerDoesNotPointAtManagedCommand", "The MCP server '{0}' exists but does not point at '{1}'.")
            .Replace("{0}", ManagedMcpServerName, StringComparison.Ordinal)
            .Replace("{1}", LidGuardMcpServerCommand.CommandName, StringComparison.Ordinal);
        return LocalizationService.GetString("ManagementLidGuardMcpServerRegistered", "LidGuard MCP server is registered.");
    }

    private static bool TrySelectMcpProviders(
        string providerText,
        string prompt,
        string distroName,
        out IReadOnlyList<AgentProvider> providers,
        out string message)
    {
        providers = [];
        if (!ManagedProviderSelection.TrySelectProviders(providerText, prompt, out var selectedProviders, out message)) return false;

        WslManagedProviderSelection.ResolveAvailableProviders(
            distroName,
            selectedProviders,
            WslManagedProviderSelection.TryHasMcpProviderConfigurationRoot,
            out providers,
            out var skippedProviderMessages);

        ManagedProviderSelection.WriteSkippedProviderMessages(skippedProviderMessages);
        if (providers.Count > 0) return true;

        ManagedProviderSelection.WriteNoAvailableProvidersFound();
        return true;
    }

    private static bool TryParseArguments(
        string commandName,
        string[] commandLineArguments,
        out string operationName,
        out string providerText,
        out Dictionary<string, string> options,
        out string message)
    {
        operationName = NormalizeOperationName(commandName);
        providerText = string.Empty;
        options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        message = string.Empty;

        if (TryGetAliasProvider(commandName, out var aliasProvider, out var aliasOperationName))
        {
            operationName = aliasOperationName;
            providerText = aliasProvider;
        }

        for (var argumentIndex = 0; argumentIndex < commandLineArguments.Length; argumentIndex++)
        {
            var argument = commandLineArguments[argumentIndex];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(providerText))
                {
                    message = LocalizationService.GetFormattedString("CommandUnexpectedArgument", argument);
                    return false;
                }

                providerText = argument;
                continue;
            }

            var separatorIndex = argument.IndexOf('=');
            if (separatorIndex > 2)
            {
                options[argument[2..separatorIndex]] = argument[(separatorIndex + 1)..];
                continue;
            }

            var optionName = argument[2..];
            if (string.IsNullOrWhiteSpace(optionName))
            {
                message = LocalizationService.GetString("CommandOptionNameRequired", "An option name is required after --.");
                return false;
            }

            if (!optionName.Equals(WslCommandUtilities.DistroOptionName, StringComparison.OrdinalIgnoreCase))
            {
                message = LocalizationService.GetFormattedString("CommandUnexpectedArgument", argument);
                return false;
            }

            if (argumentIndex + 1 >= commandLineArguments.Length || commandLineArguments[argumentIndex + 1].StartsWith("--", StringComparison.Ordinal))
            {
                message = LocalizationService.GetString("CommandRequiredOption", "The --{0} option is required.")
                    .Replace("{0}", optionName, StringComparison.Ordinal);
                return false;
            }

            options[optionName] = commandLineArguments[++argumentIndex];
        }

        return true;
    }

    private static string NormalizeOperationName(string commandName)
    {
        return commandName switch
        {
            LidGuardPipeCommands.WslMcpStatus => LidGuardPipeCommands.WslMcpStatus,
            LidGuardPipeCommands.WslMcpInstall => LidGuardPipeCommands.WslMcpInstall,
            LidGuardPipeCommands.WslMcpRemove or "wsl-mcp-uninstall" => LidGuardPipeCommands.WslMcpRemove,
            _ => commandName
        };
    }

    private static bool TryGetAliasProvider(string commandName, out string providerText, out string operationName)
    {
        providerText = string.Empty;
        operationName = string.Empty;

        var parts = commandName.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 4) return false;
        if (!parts[0].Equals("wsl", StringComparison.OrdinalIgnoreCase)) return false;
        if (!parts[2].Equals("mcp", StringComparison.OrdinalIgnoreCase)) return false;

        providerText = parts[1].ToLowerInvariant() switch
        {
            "codex" => "codex",
            "claude" => "claude",
            "copilot" => "copilot",
            _ => string.Empty
        };
        if (string.IsNullOrWhiteSpace(providerText)) return false;

        operationName = parts[3].ToLowerInvariant() switch
        {
            "status" => LidGuardPipeCommands.WslMcpStatus,
            "install" => LidGuardPipeCommands.WslMcpInstall,
            "remove" or "uninstall" => LidGuardPipeCommands.WslMcpRemove,
            _ => string.Empty
        };

        return !string.IsNullOrWhiteSpace(operationName);
    }

    private static bool TryResolveProviderCli(AgentProvider provider, string distroName, out string providerCliExecutableName)
    {
        providerCliExecutableName = WslCommandUtilities.GetProviderCliExecutableName(provider);
        if (WslCommandUtilities.TryResolveProviderCliDisplayText(distroName, provider, out var hasProviderCli, out var providerCliDisplayText) && hasProviderCli) return true;

        Console.Error.WriteLine(LocalizationService.GetFormattedString("ManagementProviderCliNotFound", ManagedProviderSelection.GetProviderDisplayName(provider), providerCliDisplayText));
        return false;
    }

    private static int RunProviderProcess(string distroName, string providerCliExecutableName, IReadOnlyList<string> arguments)
    {
        var result = WslCommandUtilities.RunCommand(distroName, providerCliExecutableName, arguments);
        if (!string.IsNullOrWhiteSpace(result.StandardOutput)) Console.Write(result.StandardOutput);
        if (!string.IsNullOrWhiteSpace(result.StandardError)) Console.Error.Write(result.StandardError);
        return result.ExitCode;
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

    private static bool TryReadCodexMcpServerSection(string sectionContent, out string serverCommand, out string[] serverArgumentValues)
    {
        serverCommand = string.Empty;
        serverArgumentValues = [];

        foreach (var rawLine in sectionContent.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            if (!TryReadTomlAssignment(rawLine, out var key, out var value)) continue;

            if (key.Equals("command", StringComparison.Ordinal))
            {
                serverCommand = ParseTomlScalarValue(value);
                continue;
            }

            if (key.Equals("args", StringComparison.Ordinal)) serverArgumentValues = ParseTomlStringArrayValue(value);
        }

        return !string.IsNullOrWhiteSpace(serverCommand) || serverArgumentValues.Length > 0;
    }

    private static string DescribeArgumentValues(string[] serverArgumentValues)
        => serverArgumentValues.Length == 0 ? LocalizationService.GetString("TextDisplayNone") : string.Join(" | ", serverArgumentValues);

    private static bool ContainsArgument(string[] serverArgumentValues, string expectedArgument)
    {
        foreach (var serverArgumentValue in serverArgumentValues)
        {
            if (serverArgumentValue.Equals(expectedArgument, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    private static bool TryReadTomlAssignment(string line, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;

        var trimmedLine = line.Trim();
        var separatorIndex = trimmedLine.IndexOf('=');
        if (separatorIndex < 0) return false;

        key = trimmedLine[..separatorIndex].Trim();
        if (string.IsNullOrWhiteSpace(key)) return false;

        value = trimmedLine[(separatorIndex + 1)..].Trim();
        return true;
    }

    private static string ParseTomlScalarValue(string value)
    {
        if (value.Length >= 2 && value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal))
        {
            return UnescapeTomlBasicString(value[1..^1]);
        }

        if (value.Length >= 2 && value.StartsWith("'", StringComparison.Ordinal) && value.EndsWith("'", StringComparison.Ordinal)) return value[1..^1];
        return value;
    }

    private static string[] ParseTomlStringArrayValue(string value)
    {
        var trimmedValue = value.Trim();
        if (trimmedValue.Length < 2 || !trimmedValue.StartsWith("[", StringComparison.Ordinal) || !trimmedValue.EndsWith("]", StringComparison.Ordinal)) return [];

        var itemValues = new List<string>();
        var itemBuilder = new StringBuilder();
        var activeQuoteCharacter = '\0';
        var isEscaping = false;

        foreach (var character in trimmedValue[1..^1])
        {
            if (activeQuoteCharacter != '\0')
            {
                itemBuilder.Append(character);

                if (activeQuoteCharacter == '"' && character == '\\' && !isEscaping)
                {
                    isEscaping = true;
                    continue;
                }

                if (character == activeQuoteCharacter && !isEscaping) activeQuoteCharacter = '\0';
                else isEscaping = false;
                continue;
            }

            if (character is '"' or '\'')
            {
                activeQuoteCharacter = character;
                itemBuilder.Append(character);
                continue;
            }

            if (character == ',')
            {
                AddTomlArrayItem(itemValues, itemBuilder.ToString());
                itemBuilder.Clear();
                continue;
            }

            itemBuilder.Append(character);
        }

        AddTomlArrayItem(itemValues, itemBuilder.ToString());
        return [.. itemValues];
    }

    private static void AddTomlArrayItem(List<string> itemValues, string itemValue)
    {
        var trimmedItemValue = itemValue.Trim();
        if (trimmedItemValue.Length == 0) return;
        itemValues.Add(ParseTomlScalarValue(trimmedItemValue));
    }

    private static string UnescapeTomlBasicString(string value)
    {
        var builder = new StringBuilder();
        for (var characterIndex = 0; characterIndex < value.Length; characterIndex++)
        {
            var character = value[characterIndex];
            if (character != '\\' || characterIndex + 1 >= value.Length)
            {
                builder.Append(character);
                continue;
            }

            var escapedCharacter = value[++characterIndex];
            builder.Append(escapedCharacter switch
            {
                'b' => '\b',
                't' => '\t',
                'n' => '\n',
                'f' => '\f',
                'r' => '\r',
                '"' => '"',
                '\\' => '\\',
                _ => escapedCharacter
            });
        }

        return builder.ToString();
    }
}
