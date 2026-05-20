using LidGuard.Localization;
using LidGuard.Mcp;
using LidGuard.Sessions;

namespace LidGuard.Commands;

internal readonly record struct ManagedMcpInspectionResult(
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

    public bool ShouldRefreshManagedMcpServer => HasNamedServerEntry && ContainsExpectedServerCommand;

    public static string GetStatusMessage(
        string configurationFilePath,
        bool configurationFileExists,
        bool hasServerEntry,
        bool matchesCurrentLidGuardExecutable,
        bool containsExpectedServerCommand,
        string parseMessage)
    {
        if (!configurationFileExists) return LocalizationService.GetFormattedString("ManagementConfigurationFileDoesNotExist", configurationFilePath);
        if (!string.IsNullOrWhiteSpace(parseMessage)) return parseMessage;
        if (!hasServerEntry) return LocalizationService.GetFormattedString("ManagementNoMcpServerNamedFound", McpConfigurationTomlUtilities.ManagedMcpServerName);
        if (!matchesCurrentLidGuardExecutable) return LocalizationService.GetString("ManagementMcpServerDoesNotPointAtCurrentExecutable", "The MCP server '{0}' exists but does not point at the current LidGuard executable.")
            .Replace("{0}", McpConfigurationTomlUtilities.ManagedMcpServerName, StringComparison.Ordinal);
        if (!containsExpectedServerCommand) return LocalizationService.GetString("ManagementMcpServerDoesNotPointAtManagedCommand", "The MCP server '{0}' exists but does not point at '{1}'.")
            .Replace("{0}", McpConfigurationTomlUtilities.ManagedMcpServerName, StringComparison.Ordinal)
            .Replace("{1}", LidGuardMcpServerCommand.CommandName, StringComparison.Ordinal);
        return LocalizationService.GetString("ManagementLidGuardMcpServerRegistered", "LidGuard MCP server is registered.");
    }

    public static int WriteProviderMcpStatus(ManagedMcpInspectionResult inspectionResult)
    {
        Console.WriteLine(LocalizationService.GetString("ManagementMcpInstallationTitle"));
        ManagementFieldWriter.WriteField("ManagementLabelProvider", "Provider", inspectionResult.Provider);
        ManagementFieldWriter.WriteField("ManagementLabelInstalled", "Installed", inspectionResult.IsManagedMcpServerInstalled);
        ManagementFieldWriter.WriteField("ManagementLabelConfig", "Config", inspectionResult.ConfigurationFilePath);
        ManagementFieldWriter.WriteField("ManagementLabelConfigExists", "Config exists", inspectionResult.ConfigurationFileExists);
        ManagementFieldWriter.WriteField("ManagementLabelCliAvailable", "CLI available", inspectionResult.HasProviderCli);
        ManagementFieldWriter.WriteField("ManagementLabelCli", "CLI", inspectionResult.ProviderCliDisplayText);
        ManagementFieldWriter.WriteField("ManagementLabelServerName", "Server name", McpConfigurationTomlUtilities.ManagedMcpServerName);
        ManagementFieldWriter.WriteField("ManagementLabelManagedServerEntry", "Managed server entry", inspectionResult.HasNamedServerEntry);
        ManagementFieldWriter.WriteField("ManagementLabelTransport", "Transport", inspectionResult.ServerType);
        ManagementFieldWriter.WriteField("ManagementLabelCommand", "Command", inspectionResult.ServerCommand);
        ManagementFieldWriter.WriteField("ManagementLabelArgs", "Args", inspectionResult.ServerArguments);
        ManagementFieldWriter.WriteField("ManagementLabelUrl", "Url", inspectionResult.ServerUrl);
        ManagementFieldWriter.WriteField("ManagementLabelMatchesCurrentLidGuardExecutable", "Matches current LidGuard executable", inspectionResult.MatchesCurrentLidGuardExecutable);
        ManagementFieldWriter.WriteField("ManagementLabelContainsMcpServerCommand", "Contains mcp-server command", inspectionResult.ContainsExpectedServerCommand);
        ManagementFieldWriter.WriteField("ManagementLabelMessage", "Message", inspectionResult.Message);
        return 0;
    }
}
