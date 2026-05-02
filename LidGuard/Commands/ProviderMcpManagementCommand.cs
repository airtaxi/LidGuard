using System.Text.Json;
using System.Text.Json.Nodes;
using LidGuard.Mcp;
using LidGuard.Hooks;

namespace LidGuard.Commands;

internal static class ProviderMcpManagementCommand
{
    private const string DefaultManagedProviderMcpServerName = "lidguard-provider";

    public static int InstallProviderMcp(IReadOnlyDictionary<string, string> options)
    {
        if (!CommandOptionReader.TryGetRequiredOption(options, "config", out var configurationFilePath, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        if (!CommandOptionReader.TryGetRequiredOption(options, "provider-name", out var providerName, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var managedExecutableReference = HookCommandUtilities.GetDefaultMcpExecutableReference();

        if (!HookCommandUtilities.HookExecutableExists(managedExecutableReference))
        {
            Console.Error.WriteLine($"LidGuard executable or command does not exist: {managedExecutableReference}");
            return 1;
        }

        if (!McpConfigurationJsonUtilities.TryLoadConfigurationRoot(configurationFilePath, true, out var rootObject, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var normalizedProviderName = providerName.Trim();
        var managedServerName = GetManagedServerName(options);
        var mcpServersObject = McpConfigurationJsonUtilities.GetOrCreateMcpServersObject(rootObject);
        var arguments = CreateProviderServerArguments(normalizedProviderName);
        mcpServersObject[managedServerName] = new JsonObject
        {
            ["type"] = "stdio",
            ["command"] = managedExecutableReference,
            ["args"] = arguments
        };

        if (!McpConfigurationJsonUtilities.TrySaveConfigurationRoot(configurationFilePath, rootObject, out message))
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
        if (!CommandOptionReader.TryGetRequiredOption(options, "config", out var configurationFilePath, out var message))
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

        if (!McpConfigurationJsonUtilities.TryLoadConfigurationRoot(configurationFilePath, false, out var rootObject, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        if (!McpConfigurationJsonUtilities.TryGetMcpServersObject(rootObject, out var mcpServersObject))
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

        if (!McpConfigurationJsonUtilities.TrySaveConfigurationRoot(configurationFilePath, rootObject, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        Console.WriteLine($"Removed provider MCP server '{managedServerName}' from {configurationFilePath}.");
        return 0;
    }

    public static int WriteProviderMcpStatus(IReadOnlyDictionary<string, string> options)
    {
        if (!CommandOptionReader.TryGetRequiredOption(options, "config", out var configurationFilePath, out var message))
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
            if (!McpConfigurationJsonUtilities.TryLoadConfigurationRoot(configurationFilePath, false, out var rootObject, out message))
            {
                Console.Error.WriteLine(message);
                return 1;
            }

            if (McpConfigurationJsonUtilities.TryGetMcpServersObject(rootObject, out var mcpServersObject)
                && mcpServersObject[managedServerName] is JsonObject serverObject)
            {
                installed = true;
                serverCommand = McpConfigurationJsonUtilities.GetJsonStringProperty(serverObject, "command");
                serverArguments = McpConfigurationJsonUtilities.DescribeJsonArray(serverObject, "args");
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

    private static string GetManagedServerName(IReadOnlyDictionary<string, string> options)
    {
        var configuredServerName = CommandOptionReader.GetOption(options, "server-name");
        return string.IsNullOrWhiteSpace(configuredServerName) ? DefaultManagedProviderMcpServerName : configuredServerName.Trim();
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


}
