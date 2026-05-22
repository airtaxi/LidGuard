using System.Text.Json;
using System.Text.Json.Nodes;
using LidGuard.Localization;

namespace LidGuard.Commands;

internal static partial class McpConfigurationJsonUtilities
{
    public static bool TryLoadConfigurationRoot(string distroName, string configurationFilePath, bool createIfMissing, out JsonObject rootObject, out string message)
    {
        rootObject = new JsonObject();
        message = string.Empty;

        if (!WslCommandUtilities.FileExists(distroName, configurationFilePath))
        {
            if (createIfMissing) return true;

            message = LocalizationService.GetString("McpConfigurationFileDoesNotExist")
                .Replace("{0}", configurationFilePath, StringComparison.Ordinal);
            return false;
        }

        if (!WslCommandUtilities.TryReadTextFile(distroName, configurationFilePath, out var content, out message)) return false;

        try
        {
            var documentOptions = new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            };
            var rootNode = JsonNode.Parse(content, documentOptions: documentOptions);

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
            message = LocalizationService.GetString("McpConfigurationJsonInvalid")
                .Replace("{0}", exception.Message, StringComparison.Ordinal);
            return false;
        }
    }

    public static bool TrySaveConfigurationRoot(string distroName, string configurationFilePath, JsonObject rootObject, out string message)
        => WslCommandUtilities.TryWriteTextFile(distroName, configurationFilePath, rootObject.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), out message);
}
