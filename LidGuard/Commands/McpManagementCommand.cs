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
        bool MatchesCurrentLidGuardExecutable,
        bool ContainsExpectedServerCommand,
        string ServerType,
        string ServerCommand,
        string ServerArguments,
        string ServerUrl,
        string Message)
    {
        public bool IsManagedMcpServerInstalled => HasNamedServerEntry && MatchesCurrentLidGuardExecutable && ContainsExpectedServerCommand;
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
        var matchesCurrentLidGuardExecutable = false;
        var containsExpectedServerCommand = false;
        var serverCommand = string.Empty;
        var serverArguments = LocalizationService.GetString("TextDisplayNone");
        var message = string.Empty;

        if (configurationFileExists)
        {
            var configurationContent = File.ReadAllText(configurationFilePath);
            if (TryGetCodexMcpServerSectionContent(configurationContent, out var sectionContent))
            {
                hasServerEntry = true;
                TryReadCodexMcpServerSection(sectionContent, out serverCommand, out var serverArgumentValues);
                serverArguments = DescribeArgumentValues(serverArgumentValues);
                matchesCurrentLidGuardExecutable = HookCommandUtilities.ExecutableReferencesMatch(serverCommand, HookCommandUtilities.GetDefaultMcpExecutableReference());
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
            GetStatusMessage(configurationFilePath, configurationFileExists, hasServerEntry, matchesCurrentLidGuardExecutable, containsExpectedServerCommand, message));
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
            if (McpConfigurationJsonUtilities.TryGetJsonMcpServerEntry(configurationContent, ManagedMcpServerName, out var serverObject, out message))
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
            GetStatusMessage(configurationFilePath, configurationFileExists, hasServerEntry, matchesCurrentLidGuardExecutable, containsExpectedServerCommand, message));
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
