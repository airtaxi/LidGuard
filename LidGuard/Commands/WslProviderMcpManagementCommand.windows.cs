using System.Text.Json.Nodes;
using LidGuard.Localization;
using LidGuard.Mcp;

namespace LidGuard.Commands;

internal static class WslProviderMcpManagementCommand
{
    public static int InstallProviderMcp(IReadOnlyDictionary<string, string> options)
    {
        if (!WslCommandUtilities.TryCreateContext(options, out var context, out var message))
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

        if (!McpConfigurationJsonUtilities.TryLoadConfigurationRoot(context.DistroName, configurationFilePath, true, out var rootObject, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var normalizedProviderName = providerName.Trim();
        var managedServerName = ProviderMcpManagementCommand.GetManagedServerName(options);
        var mcpServersObject = McpConfigurationJsonUtilities.GetOrCreateMcpServersObject(rootObject);
        mcpServersObject[managedServerName] = new JsonObject
        {
            ["type"] = "stdio",
            ["command"] = context.WslExecutablePath,
            ["args"] = ProviderMcpManagementCommand.CreateProviderServerArguments(normalizedProviderName)
        };

        if (!McpConfigurationJsonUtilities.TrySaveConfigurationRoot(context.DistroName, configurationFilePath, rootObject, out message))
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
        if (!WslCommandUtilities.TryCreateContext(options, out var context, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        if (!TryGetConfigurationFilePath(options, context.DistroName, out var configurationFilePath, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var managedServerName = ProviderMcpManagementCommand.GetManagedServerName(options);
        if (!WslCommandUtilities.FileExists(context.DistroName, configurationFilePath))
        {
            Console.WriteLine(LocalizationService.GetFormattedString("ManagementConfigurationFileDoesNotExist", configurationFilePath));
            Console.WriteLine(LocalizationService.GetFormattedString("ManagementNoProviderMcpServerNamedRemoved", managedServerName));
            return 0;
        }

        if (!McpConfigurationJsonUtilities.TryLoadConfigurationRoot(context.DistroName, configurationFilePath, false, out var rootObject, out message))
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

        if (!McpConfigurationJsonUtilities.TrySaveConfigurationRoot(context.DistroName, configurationFilePath, rootObject, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        Console.WriteLine(LocalizationService.GetFormattedString("ManagementProviderMcpRemoved", managedServerName, configurationFilePath));
        return 0;
    }

    public static int WriteProviderMcpStatus(IReadOnlyDictionary<string, string> options)
    {
        if (!WslCommandUtilities.TryCreateContext(options, out var context, out var message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        if (!TryGetConfigurationFilePath(options, context.DistroName, out var configurationFilePath, out message))
        {
            Console.Error.WriteLine(message);
            return 1;
        }

        var managedServerName = ProviderMcpManagementCommand.GetManagedServerName(options);
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
            if (!McpConfigurationJsonUtilities.TryLoadConfigurationRoot(context.DistroName, configurationFilePath, false, out var rootObject, out message))
            {
                Console.Error.WriteLine(message);
                return 1;
            }

            if (McpConfigurationJsonUtilities.TryGetMcpServersObject(rootObject, out var mcpServersObject) && mcpServersObject[managedServerName] is JsonObject serverObject)
            {
                hasManagedServerEntry = true;
                serverCommand = McpConfigurationJsonUtilities.GetJsonStringProperty(serverObject, "command");
                serverArguments = McpConfigurationJsonUtilities.DescribeJsonArray(serverObject, "args");
                configuredProviderName = ProviderMcpManagementCommand.TryGetConfiguredProviderName(serverObject, out var extractedProviderName) ? extractedProviderName : string.Empty;
                matchesCurrentLidGuardExecutable = WslCommandUtilities.ExecutableReferencesMatch(serverCommand, context.WslExecutablePath);
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
        ManagementFieldWriter.WriteField("ManagementLabelMessage", ProviderMcpManagementCommand.CreateStatusMessage(configurationFilePath, configurationFileExists, hasManagedServerEntry, matchesCurrentLidGuardExecutable, containsProviderMcpServerCommand, managedServerName, message));
        return 0;
    }

    private static bool TryGetConfigurationFilePath(IReadOnlyDictionary<string, string> options, string distroName, out string configurationFilePath, out string message)
    {
        if (!CommandOptionReader.TryGetRequiredOption(options, "config", out var configuredConfigurationFilePath, out message))
        {
            configurationFilePath = string.Empty;
            return false;
        }

        return WslCommandUtilities.TryNormalizeWslPath(distroName, configuredConfigurationFilePath, out configurationFilePath, out message);
    }

}
