using System.Text.Json;
using System.Text.Json.Nodes;
using LidGuard.Localization;
using LidGuard.Mcp;

namespace LidGuard.Commands;

internal static class WslProviderMcpManagementCommand
{
    private const string DefaultManagedProviderMcpServerName = "lidguard-provider";

    public static int InstallProviderMcp(IReadOnlyDictionary<string, string> options)
    {
        if (!TryCreateContext(options, out var context, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        if (!TryGetConfigurationFilePath(options, context.DistroName, out var configurationFilePath, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        if (!CommandOptionReader.TryGetRequiredOption(options, "provider-name", out var providerName, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        if (!TryLoadConfigurationRoot(context.DistroName, configurationFilePath, true, out var rootObject, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var normalizedProviderName = providerName.Trim();
        var managedServerName = GetManagedServerName(options);
        var mcpServersObject = McpConfigurationJsonUtilities.GetOrCreateMcpServersObject(rootObject);
        mcpServersObject[managedServerName] = new JsonObject
        {
            ["type"] = "stdio",
            ["command"] = context.WslExecutablePath,
            ["args"] = CreateProviderServerArguments(normalizedProviderName)
        };

        if (!TrySaveConfigurationRoot(context.DistroName, configurationFilePath, rootObject, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        Console.WriteLine(LocalizationService.GetFormattedString("ManagementProviderMcpInstalled", managedServerName, configurationFilePath));
        Console.WriteLine(LocalizationService.GetFormattedString("ManagementProviderName", normalizedProviderName));
        Console.WriteLine(LocalizationService.GetFormattedString("ManagementCommand", $"{context.WslExecutablePath} {ProviderMcpServerCommand.CommandName} --provider-name {normalizedProviderName}"));
        return 0;
    }

    public static int RemoveProviderMcp(IReadOnlyDictionary<string, string> options)
    {
        if (!TryCreateContext(options, out var context, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        if (!TryGetConfigurationFilePath(options, context.DistroName, out var configurationFilePath, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var managedServerName = GetManagedServerName(options);
        if (!WslCommandUtilities.FileExists(context.DistroName, configurationFilePath))
        {
            Console.WriteLine(LocalizationService.GetFormattedString("ManagementConfigurationFileDoesNotExist", configurationFilePath));
            Console.WriteLine(LocalizationService.GetFormattedString("ManagementNoProviderMcpServerNamedRemoved", managedServerName));
            return 0;
        }

        if (!TryLoadConfigurationRoot(context.DistroName, configurationFilePath, false, out var rootObject, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        if (!McpConfigurationJsonUtilities.TryGetMcpServersObject(rootObject, out var mcpServersObject))
        {
            Console.WriteLine(LocalizationService.GetString("ManagementMcpServersObjectNotFound", "The mcpServers object was not found in {0}.").Replace("{0}", configurationFilePath, StringComparison.Ordinal));
            Console.WriteLine(LocalizationService.GetFormattedString("ManagementNoProviderMcpServerNamedRemoved", managedServerName));
            return 0;
        }

        if (!mcpServersObject.Remove(managedServerName))
        {
            Console.WriteLine(LocalizationService.GetFormattedString("ManagementNoProviderMcpServerNamedFound", managedServerName, configurationFilePath));
            return 0;
        }

        if (!TrySaveConfigurationRoot(context.DistroName, configurationFilePath, rootObject, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        Console.WriteLine(LocalizationService.GetFormattedString("ManagementProviderMcpRemoved", managedServerName, configurationFilePath));
        return 0;
    }

    public static int WriteProviderMcpStatus(IReadOnlyDictionary<string, string> options)
    {
        if (!TryCreateContext(options, out var context, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        if (!TryGetConfigurationFilePath(options, context.DistroName, out var configurationFilePath, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var managedServerName = GetManagedServerName(options);
        var configurationFileExists = WslCommandUtilities.FileExists(context.DistroName, configurationFilePath);
        var hasManagedServerEntry = false;
        var installed = false;
        var matchesCurrentLidGuardExecutable = false;
        var containsProviderMcpServerCommand = false;
        var serverCommand = string.Empty;
        var serverArguments = "<none>";
        var configuredProviderName = string.Empty;

        if (configurationFileExists)
        {
            if (!TryLoadConfigurationRoot(context.DistroName, configurationFilePath, false, out var rootObject, out message))
            {
                Console.Error.WriteLine(message);
                return 1;
            }

            if (McpConfigurationJsonUtilities.TryGetMcpServersObject(rootObject, out var mcpServersObject)
                && mcpServersObject[managedServerName] is JsonObject serverObject)
            {
                hasManagedServerEntry = true;
                serverCommand = McpConfigurationJsonUtilities.GetJsonStringProperty(serverObject, "command");
                serverArguments = McpConfigurationJsonUtilities.DescribeJsonArray(serverObject, "args");
                configuredProviderName = TryGetConfiguredProviderName(serverObject, out var extractedProviderName)
                    ? extractedProviderName
                    : string.Empty;
                matchesCurrentLidGuardExecutable = WslCommandUtilities.ExecutableReferencesMatch(serverCommand, context.WslExecutablePath);
                containsProviderMcpServerCommand = McpConfigurationJsonUtilities.JsonArrayContainsStringValue(serverObject, "args", ProviderMcpServerCommand.CommandName);
                installed = matchesCurrentLidGuardExecutable && containsProviderMcpServerCommand;
            }
        }

        Console.WriteLine(LocalizationService.GetString("ManagementProviderMcpInstallationTitle"));
        WriteField("ManagementLabelConfig", "Config", configurationFilePath);
        WriteField("ManagementLabelConfigExists", "Config exists", configurationFileExists);
        WriteField("ManagementLabelServerName", "Server name", managedServerName);
        WriteField("ManagementLabelInstalled", "Installed", installed);
        WriteField("ManagementLabelManagedServerEntry", "Managed server entry", hasManagedServerEntry);
        WriteField("ManagementLabelCommand", "Command", serverCommand);
        WriteField("ManagementLabelArgs", "Args", serverArguments);
        WriteField("ManagementLabelMatchesCurrentLidGuardExecutable", "Matches current LidGuard executable", matchesCurrentLidGuardExecutable);
        WriteField("ManagementLabelContainsProviderMcpServerCommand", "Contains provider-mcp-server command", containsProviderMcpServerCommand);
        WriteField("ManagementLabelProviderName", "Provider name", configuredProviderName);
        WriteField("ManagementLabelMessage", "Message", CreateStatusMessage(configurationFilePath, configurationFileExists, hasManagedServerEntry, matchesCurrentLidGuardExecutable, containsProviderMcpServerCommand, managedServerName, message));
        return 0;
    }

    private static bool TryCreateContext(IReadOnlyDictionary<string, string> options, out WslProviderMcpContext context, out string message)
    {
        context = default;
        if (!WslCommandUtilities.TryGetDistroName(options, out var distroName, out message)) return false;
        if (!WslCommandUtilities.TryValidateWsl(distroName, out message)) return false;
        if (!WslCommandUtilities.TryGetWslLidGuardExecutablePath(distroName, out var wslExecutablePath, out message)) return false;

        context = new WslProviderMcpContext(distroName, wslExecutablePath);
        return true;
    }

    private static JsonArray CreateProviderServerArguments(string providerName)
    {
        var argumentsNode = JsonSerializer.SerializeToNode(
            [ProviderMcpServerCommand.CommandName, "--provider-name", providerName],
            ProviderMcpManagementJsonSerializerContext.Default.StringArray);
        return argumentsNode as JsonArray ?? [];
    }

    private static string CreateStatusMessage(
        string configurationFilePath,
        bool configurationFileExists,
        bool hasManagedServerEntry,
        bool matchesCurrentLidGuardExecutable,
        bool containsProviderMcpServerCommand,
        string managedServerName,
        string message)
    {
        if (!configurationFileExists) return LocalizationService.GetFormattedString("ManagementConfigurationFileDoesNotExist", configurationFilePath);
        if (!string.IsNullOrWhiteSpace(message)) return message;
        if (!hasManagedServerEntry) return LocalizationService.GetString("ManagementNoProviderMcpServerEntryFound");
        if (!matchesCurrentLidGuardExecutable) return LocalizationService.GetString("ManagementProviderMcpServerDoesNotPointAtCurrentExecutable", "The provider MCP server '{0}' exists but does not point at the current LidGuard executable.")
            .Replace("{0}", managedServerName, StringComparison.Ordinal);
        if (!containsProviderMcpServerCommand) return LocalizationService.GetString("ManagementProviderMcpServerDoesNotPointAtManagedCommand", "The provider MCP server '{0}' exists but does not point at '{1}'.")
            .Replace("{0}", managedServerName, StringComparison.Ordinal)
            .Replace("{1}", ProviderMcpServerCommand.CommandName, StringComparison.Ordinal);
        return LocalizationService.GetString("ManagementProviderMcpRegistered");
    }

    private static string GetManagedServerName(IReadOnlyDictionary<string, string> options)
    {
        var configuredServerName = CommandOptionReader.GetOption(options, "server-name");
        return string.IsNullOrWhiteSpace(configuredServerName) ? DefaultManagedProviderMcpServerName : configuredServerName.Trim();
    }

    private static bool TryGetConfigurationFilePath(
        IReadOnlyDictionary<string, string> options,
        string distroName,
        out string configurationFilePath,
        out string message)
    {
        if (!CommandOptionReader.TryGetRequiredOption(options, "config", out var configuredConfigurationFilePath, out message))
        {
            configurationFilePath = string.Empty;
            return false;
        }

        return WslCommandUtilities.TryNormalizeWslPath(distroName, configuredConfigurationFilePath, out configurationFilePath, out message);
    }

    private static bool TryGetConfiguredProviderName(JsonObject serverObject, out string providerName)
    {
        providerName = string.Empty;
        if (serverObject["args"] is not JsonArray jsonArray) return false;

        for (var itemIndex = 0; itemIndex < jsonArray.Count - 1; itemIndex++)
        {
            if (jsonArray[itemIndex] is not JsonValue jsonValue) continue;
            if (!jsonValue.TryGetValue<string>(out var stringValue)) continue;
            if (!stringValue.Equals("--provider-name", StringComparison.OrdinalIgnoreCase)) continue;

            if (jsonArray[itemIndex + 1] is not JsonValue providerNameValue
                || !providerNameValue.TryGetValue<string>(out providerName))
                return false;

            providerName = providerName.Trim();
            return !string.IsNullOrWhiteSpace(providerName);
        }

        return false;
    }

    private static bool TryLoadConfigurationRoot(
        string distroName,
        string configurationFilePath,
        bool createIfMissing,
        out JsonObject rootObject,
        out string message)
    {
        rootObject = new JsonObject();
        message = string.Empty;

        if (!WslCommandUtilities.FileExists(distroName, configurationFilePath))
        {
            if (createIfMissing) return true;

            message = LocalizationService.GetString("McpConfigurationFileDoesNotExist", "Configuration file does not exist: {0}")
                .Replace("{0}", configurationFilePath, StringComparison.Ordinal);
            return false;
        }

        if (!WslCommandUtilities.TryReadTextFile(distroName, configurationFilePath, out var content, out message)) return false;

        try
        {
            var rootNode = JsonNode.Parse(
                content,
                documentOptions: new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });

            if (rootNode is null) return true;
            if (rootNode is JsonObject existingRootObject)
            {
                rootObject = existingRootObject;
                return true;
            }

            message = LocalizationService.GetString("McpConfigurationRootNotObject", "Configuration root is not a JSON object.");
            return false;
        }
        catch (JsonException exception)
        {
            message = LocalizationService.GetString("McpConfigurationJsonInvalid", "Configuration JSON is invalid: {0}")
                .Replace("{0}", exception.Message, StringComparison.Ordinal);
            return false;
        }
    }

    private static bool TrySaveConfigurationRoot(string distroName, string configurationFilePath, JsonObject rootObject, out string message)
        => WslCommandUtilities.TryWriteTextFile(
            distroName,
            configurationFilePath,
            rootObject.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            out message);

    private static void WriteField(string labelResourceName, string fallbackLabel, object value)
    {
        var displayValue = value is bool booleanValue ? LocalizationService.DisplayBoolean(booleanValue) : LocalizationService.DisplayOptionalValue(value?.ToString() ?? string.Empty);
        Console.WriteLine(LocalizationService.GetFormattedString("ManagementField", LocalizationService.GetString(labelResourceName, fallbackLabel), displayValue));
    }

    private readonly record struct WslProviderMcpContext(string DistroName, string WslExecutablePath);
}
