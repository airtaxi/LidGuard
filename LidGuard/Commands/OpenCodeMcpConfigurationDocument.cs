using System.Text.Json;
using System.Text.Json.Nodes;
using LidGuard.Hooks;
using LidGuard.Localization;
using LidGuard.Mcp;
using LidGuard.Sessions;

namespace LidGuard.Commands;

internal static class OpenCodeMcpConfigurationDocument
{
    public static bool TryInspect(string configurationFilePath, string expectedExecutableReference, out ManagedMcpInspectionResult inspectionResult, out string message)
    {
        message = string.Empty;
        var configurationFileExists = File.Exists(configurationFilePath);
        ManagedProviderCliResolver.TryResolveProviderCliDisplayText(AgentProvider.OpenCode, out var hasProviderCli, out var providerCliDisplayText);
        if (!configurationFileExists)
        {
            inspectionResult = CreateInspection(configurationFilePath, false, hasProviderCli, providerCliDisplayText, false, false, false, string.Empty, LocalizationService.GetString("TextDisplayNone"), LocalizationService.GetFormattedString("ManagementConfigurationFileDoesNotExist", configurationFilePath));
            return true;
        }

        if (!TryLoadConfigurationRoot(configurationFilePath, false, out var rootObject, out message))
        {
            inspectionResult = CreateInspection(configurationFilePath, true, hasProviderCli, providerCliDisplayText, false, false, false, string.Empty, LocalizationService.GetString("TextDisplayNone"), message);
            return true;
        }

        if (!TryGetManagedServerObject(rootObject, out var serverObject))
        {
            inspectionResult = CreateInspection(configurationFilePath, true, hasProviderCli, providerCliDisplayText, false, false, false, string.Empty, LocalizationService.GetString("TextDisplayNone"), LocalizationService.GetFormattedString("ManagementNoMcpServerNamedFound", McpConfigurationTomlUtilities.ManagedMcpServerName));
            return true;
        }

        var serverCommand = GetCommandExecutable(serverObject);
        var serverArguments = GetCommandArguments(serverObject);
        var serverType = GetServerType(serverObject);
        var matchesCurrentLidGuardExecutable = HookCommandUtilities.ExecutableReferencesMatch(serverCommand, expectedExecutableReference);
        var containsExpectedServerCommand = ContainsArgument(serverArguments, LidGuardMcpServerCommand.CommandName) && IsLocalServer(serverType) && IsServerEnabled(serverObject);
        inspectionResult = CreateInspection(configurationFilePath, true, hasProviderCli, providerCliDisplayText, true, matchesCurrentLidGuardExecutable, containsExpectedServerCommand, serverCommand, DescribeArguments(serverArguments), ManagedMcpInspectionResult.GetStatusMessage(configurationFilePath, true, true, matchesCurrentLidGuardExecutable, containsExpectedServerCommand, string.Empty), serverType);
        return true;
    }

    public static bool TryInstall(string configurationFilePath, string executableReference, out string message)
    {
        if (!TryLoadConfigurationRoot(configurationFilePath, true, out var rootObject, out message)) return false;

        var mcpObject = GetOrCreateMcpObject(rootObject);
        mcpObject[McpConfigurationTomlUtilities.ManagedMcpServerName] = new JsonObject
        {
            ["type"] = "local",
            ["command"] = CreateCommandArray(executableReference),
            ["enabled"] = true
        };

        return TrySaveConfigurationRoot(configurationFilePath, rootObject, out message);
    }

    public static bool TryRemove(string configurationFilePath, out bool removed, out string message)
    {
        removed = false;
        if (!File.Exists(configurationFilePath))
        {
            message = LocalizationService.GetFormattedString("ManagementConfigurationFileDoesNotExist", configurationFilePath);
            return true;
        }

        if (!TryLoadConfigurationRoot(configurationFilePath, false, out var rootObject, out message)) return false;
        if (rootObject["mcp"] is not JsonObject mcpObject)
        {
            message = LocalizationService.GetString("ManagementMcpServersObjectNotFound").Replace("{0}", configurationFilePath, StringComparison.Ordinal);
            return true;
        }

        removed = mcpObject.Remove(McpConfigurationTomlUtilities.ManagedMcpServerName);
        if (!removed)
        {
            message = LocalizationService.GetFormattedString("ManagementNoMcpServerNamedFound", McpConfigurationTomlUtilities.ManagedMcpServerName);
            return true;
        }

        return TrySaveConfigurationRoot(configurationFilePath, rootObject, out message);
    }

#if LIDGUARD_WINDOWS
    public static bool TryInspectWsl(string distroName, string configurationFilePath, string expectedExecutableReference, out ManagedMcpInspectionResult inspectionResult, out string message)
    {
        message = string.Empty;
        var configurationFileExists = WslCommandUtilities.FileExists(distroName, configurationFilePath);
        WslCommandUtilities.TryResolveProviderCliDisplayText(distroName, AgentProvider.OpenCode, out var hasProviderCli, out var providerCliDisplayText);
        if (!configurationFileExists)
        {
            inspectionResult = CreateInspection(configurationFilePath, false, hasProviderCli, providerCliDisplayText, false, false, false, string.Empty, LocalizationService.GetString("TextDisplayNone"), LocalizationService.GetFormattedString("ManagementConfigurationFileDoesNotExist", configurationFilePath));
            return true;
        }

        if (!WslCommandUtilities.TryReadTextFile(distroName, configurationFilePath, out var configurationContent, out message))
        {
            inspectionResult = CreateInspection(configurationFilePath, true, hasProviderCli, providerCliDisplayText, false, false, false, string.Empty, LocalizationService.GetString("TextDisplayNone"), message);
            return true;
        }

        if (!TryLoadConfigurationRootFromContent(configurationContent, out var rootObject, out message))
        {
            inspectionResult = CreateInspection(configurationFilePath, true, hasProviderCli, providerCliDisplayText, false, false, false, string.Empty, LocalizationService.GetString("TextDisplayNone"), message);
            return true;
        }

        if (!TryGetManagedServerObject(rootObject, out var serverObject))
        {
            inspectionResult = CreateInspection(configurationFilePath, true, hasProviderCli, providerCliDisplayText, false, false, false, string.Empty, LocalizationService.GetString("TextDisplayNone"), LocalizationService.GetFormattedString("ManagementNoMcpServerNamedFound", McpConfigurationTomlUtilities.ManagedMcpServerName));
            return true;
        }

        var serverCommand = GetCommandExecutable(serverObject);
        var serverArguments = GetCommandArguments(serverObject);
        var serverType = GetServerType(serverObject);
        var matchesCurrentLidGuardExecutable = WslCommandUtilities.ExecutableReferencesMatch(serverCommand, expectedExecutableReference);
        var containsExpectedServerCommand = ContainsArgument(serverArguments, LidGuardMcpServerCommand.CommandName) && IsLocalServer(serverType) && IsServerEnabled(serverObject);
        inspectionResult = CreateInspection(configurationFilePath, true, hasProviderCli, providerCliDisplayText, true, matchesCurrentLidGuardExecutable, containsExpectedServerCommand, serverCommand, DescribeArguments(serverArguments), ManagedMcpInspectionResult.GetStatusMessage(configurationFilePath, true, true, matchesCurrentLidGuardExecutable, containsExpectedServerCommand, string.Empty), serverType);
        return true;
    }

    public static bool TryInstallWsl(string distroName, string configurationFilePath, string executableReference, out string message)
    {
        var configurationContent = string.Empty;
        var configurationFileExists = WslCommandUtilities.FileExists(distroName, configurationFilePath);
        if (configurationFileExists && !WslCommandUtilities.TryReadTextFile(distroName, configurationFilePath, out configurationContent, out message)) return false;
        if (!TryLoadConfigurationRootFromContent(configurationContent, out var rootObject, out message)) return false;

        var mcpObject = GetOrCreateMcpObject(rootObject);
        mcpObject[McpConfigurationTomlUtilities.ManagedMcpServerName] = new JsonObject
        {
            ["type"] = "local",
            ["command"] = CreateCommandArray(executableReference),
            ["enabled"] = true
        };

        return WslCommandUtilities.TryWriteTextFile(distroName, configurationFilePath, rootObject.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), out message);
    }

    public static bool TryRemoveWsl(string distroName, string configurationFilePath, out bool removed, out string message)
    {
        removed = false;
        if (!WslCommandUtilities.FileExists(distroName, configurationFilePath))
        {
            message = LocalizationService.GetFormattedString("ManagementConfigurationFileDoesNotExist", configurationFilePath);
            return true;
        }

        if (!WslCommandUtilities.TryReadTextFile(distroName, configurationFilePath, out var configurationContent, out message)) return false;
        if (!TryLoadConfigurationRootFromContent(configurationContent, out var rootObject, out message)) return false;
        if (rootObject["mcp"] is not JsonObject mcpObject)
        {
            message = LocalizationService.GetString("ManagementMcpServersObjectNotFound").Replace("{0}", configurationFilePath, StringComparison.Ordinal);
            return true;
        }

        removed = mcpObject.Remove(McpConfigurationTomlUtilities.ManagedMcpServerName);
        if (!removed)
        {
            message = LocalizationService.GetFormattedString("ManagementNoMcpServerNamedFound", McpConfigurationTomlUtilities.ManagedMcpServerName);
            return true;
        }

        return WslCommandUtilities.TryWriteTextFile(distroName, configurationFilePath, rootObject.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), out message);
    }
#endif

    private static bool ContainsArgument(IReadOnlyList<string> arguments, string expectedArgument)
    {
        foreach (var argument in arguments) if (argument.Equals(expectedArgument, StringComparison.Ordinal)) return true;
        return false;
    }

    private static JsonArray CreateCommandArray(string executableReference) =>
    [executableReference, LidGuardMcpServerCommand.CommandName];

    private static ManagedMcpInspectionResult CreateInspection(string configurationFilePath, bool configurationFileExists, bool hasProviderCli, string providerCliDisplayText, bool hasServerEntry, bool matchesCurrentLidGuardExecutable, bool containsExpectedServerCommand, string serverCommand, string serverArguments, string message, string serverType = "")
        => new(AgentProvider.OpenCode, configurationFilePath, configurationFileExists, hasProviderCli, providerCliDisplayText, hasServerEntry, matchesCurrentLidGuardExecutable, containsExpectedServerCommand, serverType, serverCommand, serverArguments, string.Empty, message);

    private static string DescribeArguments(IReadOnlyList<string> arguments) => arguments.Count == 0 ? LocalizationService.GetString("TextDisplayNone") : string.Join(" | ", arguments);

    private static string[] GetCommandArguments(JsonObject serverObject)
    {
        if (serverObject["command"] is not JsonArray commandArray || commandArray.Count < 2) return [];

        var arguments = new List<string>();
        for (var itemIndex = 1; itemIndex < commandArray.Count; itemIndex++)
        {
            if (commandArray[itemIndex] is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var value)) arguments.Add(value);
        }

        return [.. arguments];
    }

    private static string GetCommandExecutable(JsonObject serverObject)
    {
        if (serverObject["command"] is JsonArray commandArray && commandArray.Count > 0 && commandArray[0] is JsonValue commandValue && commandValue.TryGetValue<string>(out var command)) return command;
        return McpConfigurationJsonUtilities.GetJsonStringProperty(serverObject, "command");
    }

    private static string GetServerType(JsonObject serverObject) => McpConfigurationJsonUtilities.GetJsonStringProperty(serverObject, "type");

    private static bool IsLocalServer(string serverType) => serverType.Equals("local", StringComparison.OrdinalIgnoreCase);

    private static bool IsServerEnabled(JsonObject serverObject)
    {
        if (serverObject["enabled"] is not JsonValue enabledValue) return true;
        return enabledValue.TryGetValue<bool>(out var enabled) && enabled;
    }

    private static JsonObject GetOrCreateMcpObject(JsonObject rootObject)
    {
        if (rootObject["mcp"] is JsonObject existingMcpObject) return existingMcpObject;

        var mcpObject = new JsonObject();
        rootObject["mcp"] = mcpObject;
        return mcpObject;
    }

    private static bool TryGetManagedServerObject(JsonObject rootObject, out JsonObject serverObject)
    {
        serverObject = new JsonObject();
        if (rootObject["mcp"] is not JsonObject mcpObject) return false;
        if (mcpObject[McpConfigurationTomlUtilities.ManagedMcpServerName] is not JsonObject existingServerObject) return false;

        serverObject = existingServerObject;
        return true;
    }

    private static bool TryLoadConfigurationRoot(string configurationFilePath, bool createIfMissing, out JsonObject rootObject, out string message)
    {
        if (!File.Exists(configurationFilePath))
        {
            rootObject = new JsonObject();
            message = createIfMissing ? string.Empty : LocalizationService.GetFormattedString("ManagementConfigurationFileDoesNotExist", configurationFilePath);
            return createIfMissing;
        }

        return TryLoadConfigurationRootFromContent(File.ReadAllText(configurationFilePath), out rootObject, out message);
    }

    private static bool TryLoadConfigurationRootFromContent(string configurationContent, out JsonObject rootObject, out string message)
    {
        rootObject = new JsonObject();
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(configurationContent)) return true;

        try
        {
            var documentOptions = new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            };
            var rootNode = JsonNode.Parse(configurationContent, documentOptions: documentOptions);
            if (rootNode is null) return true;
            if (rootNode is JsonObject existingRootObject)
            {
                rootObject = existingRootObject;
                return true;
            }

            message = LocalizationService.GetString("McpConfigurationRootNotObject");
            return false;
        }
        catch (JsonException exception)
        {
            message = LocalizationService.GetString("McpConfigurationJsonInvalid").Replace("{0}", exception.Message, StringComparison.Ordinal);
            return false;
        }
    }

    private static bool TrySaveConfigurationRoot(string configurationFilePath, JsonObject rootObject, out string message)
    {
        try
        {
            var configurationDirectoryPath = Path.GetDirectoryName(configurationFilePath);
            if (!string.IsNullOrWhiteSpace(configurationDirectoryPath)) Directory.CreateDirectory(configurationDirectoryPath);

            File.WriteAllText(configurationFilePath, rootObject.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            message = string.Empty;
            return true;
        }
        catch (IOException exception)
        {
            message = exception.Message;
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            message = exception.Message;
            return false;
        }
    }
}
