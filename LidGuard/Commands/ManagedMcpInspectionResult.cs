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
        if (!matchesCurrentLidGuardExecutable) return LocalizationService.GetString("ManagementMcpServerDoesNotPointAtCurrentExecutable")
            .Replace("{0}", McpConfigurationTomlUtilities.ManagedMcpServerName, StringComparison.Ordinal);
        if (!containsExpectedServerCommand) return LocalizationService.GetString("ManagementMcpServerDoesNotPointAtManagedCommand")
            .Replace("{0}", McpConfigurationTomlUtilities.ManagedMcpServerName, StringComparison.Ordinal)
            .Replace("{1}", LidGuardMcpServerCommand.CommandName, StringComparison.Ordinal);
        return LocalizationService.GetString("ManagementLidGuardMcpServerRegistered");
    }

    public static int WriteProviderMcpStatus(ManagedMcpInspectionResult inspectionResult)
    {
        Console.WriteLine(LocalizationService.GetString("ManagementMcpInstallationTitle"));
        ManagementFieldWriter.WriteField("ManagementLabelProvider", inspectionResult.Provider);
        ManagementFieldWriter.WriteField("ManagementLabelInstalled", inspectionResult.IsManagedMcpServerInstalled);
        ManagementFieldWriter.WriteField("ManagementLabelConfig", inspectionResult.ConfigurationFilePath);
        ManagementFieldWriter.WriteField("ManagementLabelConfigExists", inspectionResult.ConfigurationFileExists);
        ManagementFieldWriter.WriteField("ManagementLabelCliAvailable", inspectionResult.HasProviderCli);
        ManagementFieldWriter.WriteField("ManagementLabelCli", inspectionResult.ProviderCliDisplayText);
        ManagementFieldWriter.WriteField("ManagementLabelServerName", McpConfigurationTomlUtilities.ManagedMcpServerName);
        ManagementFieldWriter.WriteField("ManagementLabelManagedServerEntry", inspectionResult.HasNamedServerEntry);
        ManagementFieldWriter.WriteField("ManagementLabelTransport", inspectionResult.ServerType);
        ManagementFieldWriter.WriteField("ManagementLabelCommand", inspectionResult.ServerCommand);
        ManagementFieldWriter.WriteField("ManagementLabelArgs", inspectionResult.ServerArguments);
        ManagementFieldWriter.WriteField("ManagementLabelUrl", inspectionResult.ServerUrl);
        ManagementFieldWriter.WriteField("ManagementLabelMatchesCurrentLidGuardExecutable", inspectionResult.MatchesCurrentLidGuardExecutable);
        ManagementFieldWriter.WriteField("ManagementLabelContainsMcpServerCommand", inspectionResult.ContainsExpectedServerCommand);
        ManagementFieldWriter.WriteField("ManagementLabelMessage", inspectionResult.Message);
        return 0;
    }
}
