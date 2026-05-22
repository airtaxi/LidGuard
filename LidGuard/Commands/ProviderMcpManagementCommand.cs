using System.Text.Json;
using System.Text.Json.Nodes;
using LidGuard.Mcp;
using LidGuard.Hooks;
using LidGuard.Localization;

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
            Console.Error.WriteLine(LocalizationService.GetString("ManagementLidGuardExecutableMissing").Replace("{0}", managedExecutableReference, StringComparison.Ordinal));
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

        Console.WriteLine(LocalizationService.GetFormattedString("ManagementProviderMcpInstalled", managedServerName, configurationFilePath));
        Console.WriteLine(LocalizationService.GetFormattedString("ManagementProviderName", normalizedProviderName));
        Console.WriteLine(LocalizationService.GetFormattedString("ManagementCommand", $"{managedExecutableReference} {ProviderMcpServerCommand.CommandName} --provider-name {normalizedProviderName}"));
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
            Console.WriteLine(LocalizationService.GetFormattedString("ManagementConfigurationFileDoesNotExist", configurationFilePath));
            Console.WriteLine(LocalizationService.GetFormattedString("ManagementNoProviderMcpServerNamedRemoved", managedServerName));
            return 0;
        }

        if (!McpConfigurationJsonUtilities.TryLoadConfigurationRoot(configurationFilePath, false, out var rootObject, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        if (!McpConfigurationJsonUtilities.TryGetMcpServersObject(rootObject, out var mcpServersObject))
        {
            Console.WriteLine(LocalizationService.GetString("ManagementMcpServersObjectNotFound").Replace("{0}", configurationFilePath, StringComparison.Ordinal));
            Console.WriteLine(LocalizationService.GetFormattedString("ManagementNoProviderMcpServerNamedRemoved", managedServerName));
            return 0;
        }

        if (!mcpServersObject.Remove(managedServerName))
        {
            Console.WriteLine(LocalizationService.GetFormattedString("ManagementNoProviderMcpServerNamedFound", managedServerName, configurationFilePath));
            return 0;
        }

        if (!McpConfigurationJsonUtilities.TrySaveConfigurationRoot(configurationFilePath, rootObject, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        Console.WriteLine(LocalizationService.GetFormattedString("ManagementProviderMcpRemoved", managedServerName, configurationFilePath));
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
        var hasManagedServerEntry = false;
        var installed = false;
        var matchesCurrentLidGuardExecutable = false;
        var containsProviderMcpServerCommand = false;
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
                hasManagedServerEntry = true;
                serverCommand = McpConfigurationJsonUtilities.GetJsonStringProperty(serverObject, "command");
                serverArguments = McpConfigurationJsonUtilities.DescribeJsonArray(serverObject, "args");
                configuredProviderName = TryGetConfiguredProviderName(serverObject, out var extractedProviderName)
                    ? extractedProviderName
                    : string.Empty;
                matchesCurrentLidGuardExecutable = HookCommandUtilities.ExecutableReferencesMatch(serverCommand, HookCommandUtilities.GetDefaultMcpExecutableReference());
                containsProviderMcpServerCommand = McpConfigurationJsonUtilities.JsonArrayContainsStringValue(serverObject, "args", ProviderMcpServerCommand.CommandName);
                installed = matchesCurrentLidGuardExecutable && containsProviderMcpServerCommand;
            }
        }

        Console.WriteLine(LocalizationService.GetString("ManagementProviderMcpInstallationTitle"));
        ManagementFieldWriter.WriteField("ManagementLabelConfig", configurationFilePath);
        ManagementFieldWriter.WriteField("ManagementLabelConfigExists", configurationFileExists);
        ManagementFieldWriter.WriteField("ManagementLabelServerName", managedServerName);
        ManagementFieldWriter.WriteField("ManagementLabelInstalled", installed);
        ManagementFieldWriter.WriteField("ManagementLabelManagedServerEntry", hasManagedServerEntry);
        ManagementFieldWriter.WriteField("ManagementLabelCommand", serverCommand);
        ManagementFieldWriter.WriteField("ManagementLabelArgs", serverArguments);
        ManagementFieldWriter.WriteField("ManagementLabelMatchesCurrentLidGuardExecutable", matchesCurrentLidGuardExecutable);
        ManagementFieldWriter.WriteField("ManagementLabelContainsProviderMcpServerCommand", containsProviderMcpServerCommand);
        ManagementFieldWriter.WriteField("ManagementLabelProviderName", configuredProviderName);
        ManagementFieldWriter.WriteField("ManagementLabelMessage", CreateStatusMessage(configurationFilePath, configurationFileExists, hasManagedServerEntry, matchesCurrentLidGuardExecutable, containsProviderMcpServerCommand, managedServerName, message));
        return 0;
    }

    internal static JsonArray CreateProviderServerArguments(string providerName)
    {
        var argumentsNode = JsonSerializer.SerializeToNode(
            [ProviderMcpServerCommand.CommandName, "--provider-name", providerName],
            ProviderMcpManagementJsonSerializerContext.Default.StringArray);
        return argumentsNode as JsonArray ?? [];
    }

    internal static string CreateStatusMessage(
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
        if (!matchesCurrentLidGuardExecutable) return LocalizationService.GetString("ManagementProviderMcpServerDoesNotPointAtCurrentExecutable")
            .Replace("{0}", managedServerName, StringComparison.Ordinal);
        if (!containsProviderMcpServerCommand) return LocalizationService.GetString("ManagementProviderMcpServerDoesNotPointAtManagedCommand")
            .Replace("{0}", managedServerName, StringComparison.Ordinal)
            .Replace("{1}", ProviderMcpServerCommand.CommandName, StringComparison.Ordinal);
        return LocalizationService.GetString("ManagementProviderMcpRegistered");
    }

    internal static string GetManagedServerName(IReadOnlyDictionary<string, string> options)
    {
        var configuredServerName = CommandOptionReader.GetOption(options, "server-name");
        return string.IsNullOrWhiteSpace(configuredServerName) ? DefaultManagedProviderMcpServerName : configuredServerName.Trim();
    }

    internal static bool TryGetConfiguredProviderName(JsonObject serverObject, out string providerName)
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
