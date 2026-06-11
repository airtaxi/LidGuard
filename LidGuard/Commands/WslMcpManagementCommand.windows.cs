using LidGuard.Hooks;
using LidGuard.Ipc;
using LidGuard.Localization;
using LidGuard.Mcp;
using LidGuard.Sessions;

namespace LidGuard.Commands;

internal static class WslMcpManagementCommand
{
    public static bool IsCommandName(string commandName) => (commandName is LidGuardPipeCommands.WslMcpStatus or LidGuardPipeCommands.WslMcpInstall or LidGuardPipeCommands.WslMcpRemove or "wsl-mcp-uninstall") || TryGetAliasProvider(commandName, out _, out _);

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
        if (!TrySelectMcpProviders(providerText, LocalizationService.GetString("ManagementPromptMcpStatus"), distroName, out var providers, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var exitCode = 0;
        foreach (var provider in providers)
        {
            if (providers.Count > 1) Console.WriteLine(LocalizationService.GetFormattedString("ManagementMcpStatusTitle", ManagedProviderSelection.GetProviderDisplayName(provider)));

            var providerExitCode = TryInspectProviderMcp(provider, distroName, wslExecutablePath, out var inspectionResult) ? ManagedMcpInspectionResult.WriteProviderMcpStatus(inspectionResult) : WriteUnsupportedProvider();

            if (providerExitCode != 0) exitCode = providerExitCode;
            if (providers.Count > 1) Console.WriteLine();
        }

        return exitCode;
    }

    private static int InstallMcp(string providerText, string distroName, string wslExecutablePath)
    {
        if (!TrySelectMcpProviders(providerText, LocalizationService.GetString("ManagementPromptMcpInstall"), distroName, out var providers, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var exitCode = 0;
        foreach (var provider in providers)
        {
            if (providers.Count > 1) Console.WriteLine(LocalizationService.GetFormattedString("ManagementInstallingMcpServer", ManagedProviderSelection.GetProviderDisplayName(provider)));
            var providerExitCode = TryInspectProviderMcp(provider, distroName, wslExecutablePath, out var inspectionResult) ? InstallProviderMcp(provider, distroName, wslExecutablePath, inspectionResult) : WriteUnsupportedProvider();
            if (providerExitCode != 0) exitCode = providerExitCode;
            if (providers.Count > 1) Console.WriteLine();
        }

        return exitCode;
    }

    private static int RemoveMcp(string providerText, string distroName, string wslExecutablePath)
    {
        if (!TrySelectMcpProviders(providerText, LocalizationService.GetString("ManagementPromptMcpRemove"), distroName, out var providers, out var message))
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

    private static int InstallProviderMcp(AgentProvider provider, string distroName, string wslExecutablePath, ManagedMcpInspectionResult inspectionResult)
    {
        if (inspectionResult.ShouldRefreshManagedMcpServer)
        {
            Console.WriteLine(LocalizationService.GetString("ManagementExistingMcpServerRefreshing").Replace("{0}", ManagedProviderSelection.GetProviderDisplayName(provider), StringComparison.Ordinal));

            var removeExitCode = RemoveProviderMcp(provider, distroName);
            if (removeExitCode != 0)
            {
                Console.Error.WriteLine(LocalizationService.GetString("ManagementSkippingMcpInstallAfterRemoveFailure").Replace("{0}", ManagedProviderSelection.GetProviderDisplayName(provider), StringComparison.Ordinal));
                return removeExitCode;
            }
        }

        return AddProviderMcp(provider, distroName, wslExecutablePath);
    }

    private static int AddProviderMcp(AgentProvider provider, string distroName, string wslExecutablePath)
    {
        if (provider == AgentProvider.OpenCode) return AddOpenCodeMcp(distroName, wslExecutablePath);

        if (!TryResolveProviderCli(provider, distroName, out var providerCliExecutableName)) return 1;

        var processArguments = McpManagementCommand.CreateProviderMcpInstallArguments(provider, wslExecutablePath);
        if (processArguments.Count == 0) return WriteUnsupportedProvider();

        return RunProviderProcess(distroName, providerCliExecutableName, processArguments);
    }

    private static int RemoveProviderMcp(AgentProvider provider, string distroName)
    {
        if (provider == AgentProvider.OpenCode) return RemoveOpenCodeMcp(distroName);

        if (!TryResolveProviderCli(provider, distroName, out var providerCliExecutableName)) return 1;

        var processArguments = McpManagementCommand.CreateProviderMcpRemoveArguments(provider);
        if (processArguments.Count == 0) return WriteUnsupportedProvider();

        return RunProviderProcess(distroName, providerCliExecutableName, processArguments);
    }

    private static int AddOpenCodeMcp(string distroName, string wslExecutablePath)
    {
        if (!WslProviderConfigurationRoots.TryGetMcpConfigurationFilePath(distroName, AgentProvider.OpenCode, out var configurationFilePath, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        if (!OpenCodeMcpConfigurationDocument.TryInstallWsl(distroName, configurationFilePath, wslExecutablePath, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        Console.WriteLine(LocalizationService.GetFormattedString("ManagementMcpServerInstalled", McpConfigurationTomlUtilities.ManagedMcpServerName, configurationFilePath));
        return 0;
    }

    private static int RemoveOpenCodeMcp(string distroName)
    {
        if (!WslProviderConfigurationRoots.TryGetMcpConfigurationFilePath(distroName, AgentProvider.OpenCode, out var configurationFilePath, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        if (!OpenCodeMcpConfigurationDocument.TryRemoveWsl(distroName, configurationFilePath, out var removed, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        Console.WriteLine(removed ? LocalizationService.GetFormattedString("ManagementMcpServerRemoved", McpConfigurationTomlUtilities.ManagedMcpServerName, configurationFilePath) : message);
        return 0;
    }

    private static ManagedMcpInspectionResult InspectCodexMcp(string distroName, string wslExecutablePath)
    {
        if (!WslProviderConfigurationRoots.TryGetMcpConfigurationFilePath(distroName, AgentProvider.Codex, out var configurationFilePath, out var message)) configurationFilePath = string.Empty;

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
                message = LocalizationService.GetString("McpConfigurationJsonInvalid")
                    .Replace("{0}", message, StringComparison.Ordinal);
            }
            else if (McpConfigurationTomlUtilities.TryGetCodexMcpServerSectionContent(configurationContent, out var sectionContent))
            {
                hasServerEntry = true;
                McpConfigurationTomlUtilities.TryReadCodexMcpServerSection(sectionContent, out serverCommand, out var serverArgumentValues);
                serverArguments = McpConfigurationTomlUtilities.DescribeArgumentValues(serverArgumentValues);
                matchesCurrentLidGuardExecutable = WslCommandUtilities.ExecutableReferencesMatch(serverCommand, wslExecutablePath);
                containsExpectedServerCommand = McpConfigurationTomlUtilities.ContainsArgument(serverArgumentValues, LidGuardMcpServerCommand.CommandName);
            }
            else
            {
                message = LocalizationService.GetFormattedString("ManagementNoMcpServerNamedFound", McpConfigurationTomlUtilities.ManagedMcpServerName);
            }
        }
        else message = LocalizationService.GetFormattedString("ManagementConfigurationFileDoesNotExist", configurationFilePath);

        return new ManagedMcpInspectionResult(AgentProvider.Codex, configurationFilePath, configurationFileExists, hasProviderCli, providerCliDisplayText, hasServerEntry, matchesCurrentLidGuardExecutable, containsExpectedServerCommand, string.Empty, serverCommand, serverArguments, string.Empty, ManagedMcpInspectionResult.GetStatusMessage(configurationFilePath, configurationFileExists, hasServerEntry, matchesCurrentLidGuardExecutable, containsExpectedServerCommand, message));
    }

    private static ManagedMcpInspectionResult InspectJsonProviderMcp(AgentProvider provider, string distroName, string wslExecutablePath)
    {
        if (!WslProviderConfigurationRoots.TryGetMcpConfigurationFilePath(distroName, provider, out var configurationFilePath, out var message)) configurationFilePath = string.Empty;

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
                message = LocalizationService.GetString("McpConfigurationJsonInvalid")
                    .Replace("{0}", message, StringComparison.Ordinal);
            }
            else if (McpConfigurationJsonUtilities.TryGetJsonMcpServerEntry(configurationContent, McpConfigurationTomlUtilities.ManagedMcpServerName, out var serverObject, out message))
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

        return new ManagedMcpInspectionResult(provider, configurationFilePath, configurationFileExists, hasProviderCli, providerCliDisplayText, hasServerEntry, matchesCurrentLidGuardExecutable, containsExpectedServerCommand, serverType, serverCommand, serverArguments, serverUrl, ManagedMcpInspectionResult.GetStatusMessage(configurationFilePath, configurationFileExists, hasServerEntry, matchesCurrentLidGuardExecutable, containsExpectedServerCommand, message));
    }

    private static bool TryInspectProviderMcp(AgentProvider provider, string distroName, string wslExecutablePath, out ManagedMcpInspectionResult inspectionResult)
    {
        inspectionResult = provider switch
        {
            AgentProvider.Codex => InspectCodexMcp(distroName, wslExecutablePath),
            AgentProvider.Claude => InspectJsonProviderMcp(AgentProvider.Claude, distroName, wslExecutablePath),
            AgentProvider.GitHubCopilot => InspectJsonProviderMcp(AgentProvider.GitHubCopilot, distroName, wslExecutablePath),
            AgentProvider.OpenCode => InspectOpenCodeMcp(distroName, wslExecutablePath),
            _ => default
        };

        return provider is AgentProvider.Codex or AgentProvider.Claude or AgentProvider.GitHubCopilot or AgentProvider.OpenCode;
    }

    private static ManagedMcpInspectionResult InspectOpenCodeMcp(string distroName, string wslExecutablePath)
    {
        if (!WslProviderConfigurationRoots.TryGetMcpConfigurationFilePath(distroName, AgentProvider.OpenCode, out var configurationFilePath, out var message)) configurationFilePath = string.Empty;
        OpenCodeMcpConfigurationDocument.TryInspectWsl(distroName, configurationFilePath, wslExecutablePath, out var inspectionResult, out message);
        return inspectionResult;
    }

    private static bool TrySelectMcpProviders(string providerText, string prompt, string distroName, out IReadOnlyList<AgentProvider> providers, out string message)
    {
        providers = [];
        if (!ManagedProviderSelection.TrySelectProviders(providerText, prompt, out var selectedProviders, out message)) return false;

        WslManagedProviderSelection.ResolveAvailableProviders(distroName, selectedProviders, WslManagedProviderSelection.TryHasMcpProviderConfigurationRoot, out providers, out var skippedProviderMessages);

        ManagedProviderSelection.WriteSkippedProviderMessages(skippedProviderMessages);
        if (providers.Count > 0) return true;

        ManagedProviderSelection.WriteNoAvailableProvidersFound();
        return true;
    }

    private static bool TryParseArguments(string commandName, string[] commandLineArguments, out string operationName, out string providerText, out Dictionary<string, string> options, out string message)
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
                message = LocalizationService.GetString("CommandOptionNameRequired");
                return false;
            }

            if (!optionName.Equals(WslCommandUtilities.DistroOptionName, StringComparison.OrdinalIgnoreCase))
            {
                message = LocalizationService.GetFormattedString("CommandUnexpectedArgument", argument);
                return false;
            }

            if (argumentIndex + 1 >= commandLineArguments.Length || commandLineArguments[argumentIndex + 1].StartsWith("--", StringComparison.Ordinal))
            {
                message = LocalizationService.GetString("CommandRequiredOption")
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
            "opencode" => "opencode",
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
}
