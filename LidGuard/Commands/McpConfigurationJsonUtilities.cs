using System.Text.Json;
using System.Text.Json.Nodes;
using LidGuard.Localization;

namespace LidGuard.Commands;

internal static partial class McpConfigurationJsonUtilities
{
    public static string DescribeJsonArray(JsonObject jsonObject, string propertyName)
    {
        if (jsonObject[propertyName] is not JsonArray jsonArray || jsonArray.Count == 0) return LocalizationService.GetString("TextDisplayNone");

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

    public static JsonObject GetOrCreateMcpServersObject(JsonObject rootObject)
    {
        if (rootObject["mcpServers"] is JsonObject existingMcpServersObject) return existingMcpServersObject;

        var newMcpServersObject = new JsonObject();
        rootObject["mcpServers"] = newMcpServersObject;
        return newMcpServersObject;
    }

    public static string GetJsonStringProperty(JsonObject jsonObject, string propertyName)
    {
        var valueNode = jsonObject[propertyName];
        return valueNode is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var value) ? value : string.Empty;
    }

    public static bool JsonArrayContainsStringValue(JsonObject jsonObject, string propertyName, string expectedValue)
    {
        if (jsonObject[propertyName] is not JsonArray jsonArray || string.IsNullOrWhiteSpace(expectedValue)) return false;

        foreach (var item in jsonArray)
        {
            if (item is not JsonValue jsonValue) continue;
            if (!jsonValue.TryGetValue<string>(out var stringValue)) continue;
            if (stringValue.Equals(expectedValue, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    public static bool TryGetMcpServersObject(JsonObject rootObject, out JsonObject mcpServersObject)
    {
        if (rootObject["mcpServers"] is JsonObject existingMcpServersObject)
        {
            mcpServersObject = existingMcpServersObject;
            return true;
        }

        mcpServersObject = new JsonObject();
        return false;
    }

    public static bool TryGetJsonMcpServerEntry(
        string configurationContent,
        string managedServerName,
        out JsonObject serverObject,
        out string message)
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
                message = LocalizationService.GetString("McpConfigurationRootNotObject", "Configuration root is not a JSON object.");
                return false;
            }

            rootObject = existingRootObject;
        }
        catch (JsonException exception)
        {
            message = LocalizationService.GetString("McpConfigurationJsonInvalid", "Configuration JSON is invalid: {0}")
                .Replace("{0}", exception.Message, StringComparison.Ordinal);
            return false;
        }

        if (rootObject["mcpServers"] is not JsonObject mcpServersObject)
        {
            message = LocalizationService.GetString("McpConfigurationServersObjectNotFound", "The mcpServers object was not found.");
            return false;
        }

        if (mcpServersObject[managedServerName] is not JsonObject existingServerObject)
        {
            message = LocalizationService.GetString("McpConfigurationNamedServerNotFound", "No MCP server named '{0}' was found.")
                .Replace("{0}", managedServerName, StringComparison.Ordinal);
            return false;
        }

        serverObject = existingServerObject;
        return true;
    }

    public static bool TryLoadConfigurationRoot(
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

            message = LocalizationService.GetString("McpConfigurationFileDoesNotExist", "Configuration file does not exist: {0}")
                .Replace("{0}", configurationFilePath, StringComparison.Ordinal);
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

            message = LocalizationService.GetString("McpConfigurationRootNotObject", "Configuration root is not a JSON object.");
            return false;
        }
        catch (JsonException exception)
        {
            message = LocalizationService.GetString("McpConfigurationJsonInvalid", "Configuration JSON is invalid: {0}")
                .Replace("{0}", exception.Message, StringComparison.Ordinal);
            return false;
        }
        catch (IOException exception)
        {
            message = exception.Message;
            return false;
        }
    }

    public static bool TrySaveConfigurationRoot(string configurationFilePath, JsonObject rootObject, out string message)
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
