using System.Text.Json;
using System.Text.Json.Nodes;
using LidGuard.Mcp;
using LidGuardLib.Windows.Hooks;

namespace LidGuard.Commands;

internal static class ProviderMcpManagementCommand
{
    private const string DefaultManagedProviderMcpServerName = "lidguard-provider";

    public static int InstallProviderMcp(IReadOnlyDictionary<string, string> options)
    {
        if (!TryGetRequiredOption(options, "config", out var configurationFilePath, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        if (!TryGetRequiredOption(options, "provider-name", out var providerName, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var managedExecutableReference = WindowsHookCommandUtilities.GetDefaultMcpExecutableReference();

        if (!WindowsHookCommandUtilities.HookExecutableExists(managedExecutableReference))
        {
            Console.Error.WriteLine($"LidGuard executable or command does not exist: {managedExecutableReference}");
            return 1;
        }

        if (!TryLoadConfigurationRoot(configurationFilePath, true, out var rootObject, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var normalizedProviderName = providerName.Trim();
        var managedServerName = GetManagedServerName(options);
        var mcpServersObject = GetOrCreateMcpServersObject(rootObject);
        var arguments = CreateProviderServerArguments(normalizedProviderName);
        mcpServersObject[managedServerName] = new JsonObject
        {
            ["type"] = "stdio",
            ["command"] = managedExecutableReference,
            ["args"] = arguments
        };

        if (!TrySaveConfigurationRoot(configurationFilePath, rootObject, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        Console.WriteLine($"Installed provider MCP server '{managedServerName}' in {configurationFilePath}.");
        Console.WriteLine($"Provider name: {normalizedProviderName}");
        Console.WriteLine($"Command: {managedExecutableReference} {ProviderMcpServerCommand.CommandName} --provider-name {normalizedProviderName}");
        return 0;
    }

    public static int RemoveProviderMcp(IReadOnlyDictionary<string, string> options)
    {
        if (!TryGetRequiredOption(options, "config", out var configurationFilePath, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var managedServerName = GetManagedServerName(options);
        if (!File.Exists(configurationFilePath))
        {
            Console.WriteLine($"Configuration file does not exist: {configurationFilePath}");
            Console.WriteLine($"No provider MCP server named '{managedServerName}' was removed.");
            return 0;
        }

        if (!TryLoadConfigurationRoot(configurationFilePath, false, out var rootObject, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        if (!TryGetMcpServersObject(rootObject, out var mcpServersObject))
        {
            Console.WriteLine($"The mcpServers object was not found in {configurationFilePath}.");
            Console.WriteLine($"No provider MCP server named '{managedServerName}' was removed.");
            return 0;
        }

        if (!mcpServersObject.Remove(managedServerName))
        {
            Console.WriteLine($"No provider MCP server named '{managedServerName}' was found in {configurationFilePath}.");
            return 0;
        }

        if (!TrySaveConfigurationRoot(configurationFilePath, rootObject, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        Console.WriteLine($"Removed provider MCP server '{managedServerName}' from {configurationFilePath}.");
        return 0;
    }

    public static int WriteProviderMcpStatus(IReadOnlyDictionary<string, string> options)
    {
        if (!TryGetRequiredOption(options, "config", out var configurationFilePath, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var managedServerName = GetManagedServerName(options);
        var configurationFileExists = File.Exists(configurationFilePath);
        var installed = false;
        var serverCommand = string.Empty;
        var serverArguments = "<none>";
        var configuredProviderName = string.Empty;

        if (configurationFileExists)
        {
            if (!TryLoadConfigurationRoot(configurationFilePath, false, out var rootObject, out message))
            {
                Console.Error.WriteLine(message);
                return 1;
            }

            if (TryGetMcpServersObject(rootObject, out var mcpServersObject)
                && mcpServersObject[managedServerName] is JsonObject serverObject)
            {
                installed = true;
                serverCommand = GetJsonStringProperty(serverObject, "command");
                serverArguments = DescribeJsonArray(serverObject, "args");
                configuredProviderName = TryGetConfiguredProviderName(serverObject, out var extractedProviderName)
                    ? extractedProviderName
                    : string.Empty;
            }
        }

        Console.WriteLine("Provider MCP installation:");
        Console.WriteLine($"  Config: {configurationFilePath}");
        Console.WriteLine($"  Config exists: {configurationFileExists}");
        Console.WriteLine($"  Server name: {managedServerName}");
        Console.WriteLine($"  Installed: {installed}");
        Console.WriteLine($"  Command: {(string.IsNullOrWhiteSpace(serverCommand) ? "<none>" : serverCommand)}");
        Console.WriteLine($"  Args: {serverArguments}");
        Console.WriteLine($"  Provider name: {(string.IsNullOrWhiteSpace(configuredProviderName) ? "<none>" : configuredProviderName)}");
        Console.WriteLine($"  Message: {CreateStatusMessage(configurationFilePath, configurationFileExists, installed, message)}");
        return 0;
    }

    private static JsonArray CreateProviderServerArguments(string providerName)
    {
        var argumentsNode = JsonSerializer.SerializeToNode(
            [ProviderMcpServerCommand.CommandName, "--provider-name", providerName],
            ProviderMcpManagementJsonSerializerContext.Default.StringArray);
        return argumentsNode as JsonArray ?? [];
    }

    private static string CreateStatusMessage(string configurationFilePath, bool configurationFileExists, bool installed, string message)
    {
        if (!configurationFileExists) return $"Configuration file does not exist: {configurationFilePath}";
        if (!string.IsNullOrWhiteSpace(message)) return message;
        if (!installed) return "No managed provider MCP server entry was found.";
        return "Managed provider MCP server is registered.";
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

    private static JsonObject GetOrCreateMcpServersObject(JsonObject rootObject)
    {
        if (rootObject["mcpServers"] is JsonObject existingMcpServersObject) return existingMcpServersObject;

        var newMcpServersObject = new JsonObject();
        rootObject["mcpServers"] = newMcpServersObject;
        return newMcpServersObject;
    }

    private static string GetManagedServerName(IReadOnlyDictionary<string, string> options)
    {
        var configuredServerName = GetOption(options, "server-name");
        return string.IsNullOrWhiteSpace(configuredServerName) ? DefaultManagedProviderMcpServerName : configuredServerName.Trim();
    }

    private static string GetJsonStringProperty(JsonObject jsonObject, string propertyName)
    {
        var valueNode = jsonObject[propertyName];
        return valueNode is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var value) ? value : string.Empty;
    }

    private static string GetOption(IReadOnlyDictionary<string, string> options, params string[] optionNames)
    {
        foreach (var optionName in optionNames)
        {
            if (options.TryGetValue(optionName, out var optionValue)) return optionValue;
        }

        return string.Empty;
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

    private static bool TryGetMcpServersObject(JsonObject rootObject, out JsonObject mcpServersObject)
    {
        if (rootObject["mcpServers"] is JsonObject existingMcpServersObject)
        {
            mcpServersObject = existingMcpServersObject;
            return true;
        }

        mcpServersObject = new JsonObject();
        return false;
    }

    private static bool TryGetRequiredOption(
        IReadOnlyDictionary<string, string> options,
        string optionName,
        out string value,
        out string message)
    {
        value = GetOption(options, optionName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            value = value.Trim();
            message = string.Empty;
            return true;
        }

        message = $"The --{optionName} option is required.";
        return false;
    }

    private static bool TryLoadConfigurationRoot(
        string configurationFilePath,
        bool createIfMissing,
        out JsonObject rootObject,
        out string message)
    {
        rootObject = new JsonObject();
        message = string.Empty;

        if (!File.Exists(configurationFilePath))
        {
            if (createIfMissing) return true;

            message = $"Configuration file does not exist: {configurationFilePath}";
            return false;
        }

        try
        {
            var rootNode = JsonNode.Parse(
                File.ReadAllText(configurationFilePath),
                documentOptions: new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });

            if (rootNode is null)
            {
                rootObject = new JsonObject();
                return true;
            }

            if (rootNode is JsonObject existingRootObject)
            {
                rootObject = existingRootObject;
                return true;
            }

            message = "Configuration root is not a JSON object.";
            return false;
        }
        catch (JsonException exception)
        {
            message = $"Configuration JSON is invalid: {exception.Message}";
            return false;
        }
        catch (IOException exception)
        {
            message = exception.Message;
            return false;
        }
    }

    private static bool TrySaveConfigurationRoot(string configurationFilePath, JsonObject rootObject, out string message)
    {
        message = string.Empty;

        try
        {
            var configurationDirectoryPath = Path.GetDirectoryName(configurationFilePath);
            if (!string.IsNullOrWhiteSpace(configurationDirectoryPath)) Directory.CreateDirectory(configurationDirectoryPath);

            File.WriteAllText(
                configurationFilePath,
                rootObject.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
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
