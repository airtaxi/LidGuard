using LidGuard.Sessions;

namespace LidGuard.Hooks;

public sealed class OpenCodeHookInstaller : HookInstallerBase
{
    private const string OpenCodeConfigurationDirectoryEnvironmentVariableName = "OPENCODE_CONFIG_DIR";
    private const string OpenCodeConfigurationDirectoryName = ".config";
    private const string OpenCodeConfigurationSubdirectoryName = "opencode";
    private const string OpenCodeConfigurationFileName = "opencode.json";
    private const string OpenCodeConfigurationJsoncFileName = "opencode.jsonc";
    private const string PluginsDirectoryName = "plugins";
    private const string ManagedPluginFileName = "lidguard.js";

    protected override AgentProvider Provider => AgentProvider.OpenCode;

    protected override string ProviderDisplayName => "OpenCode";

    protected override string DefaultHookCommandName => "opencode-hook";

    protected override bool ShouldDeleteConfigurationFileWhenRemoved => true;

    public static string GetDefaultOpenCodeConfigurationDirectoryPath()
    {
        var opencodeConfigurationDirectoryPath = Environment.GetEnvironmentVariable(OpenCodeConfigurationDirectoryEnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(opencodeConfigurationDirectoryPath))
        {
            var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            opencodeConfigurationDirectoryPath = Path.Combine(userProfilePath, OpenCodeConfigurationDirectoryName, OpenCodeConfigurationSubdirectoryName);
        }

        return Path.GetFullPath(opencodeConfigurationDirectoryPath);
    }

    public static string GetDefaultOpenCodeConfigurationFilePath()
    {
        var configuredConfigurationFilePath = Environment.GetEnvironmentVariable("OPENCODE_CONFIG");
        if (!string.IsNullOrWhiteSpace(configuredConfigurationFilePath)) return Path.GetFullPath(configuredConfigurationFilePath);

        var configurationDirectoryPath = GetDefaultOpenCodeConfigurationDirectoryPath();
        var jsonConfigurationFilePath = Path.Combine(configurationDirectoryPath, OpenCodeConfigurationFileName);
        if (File.Exists(jsonConfigurationFilePath)) return jsonConfigurationFilePath;
        return Path.Combine(configurationDirectoryPath, OpenCodeConfigurationJsoncFileName);
    }

    public static string GetDefaultOpenCodePluginsDirectoryPath() => Path.Combine(GetDefaultOpenCodeConfigurationDirectoryPath(), PluginsDirectoryName);

    public static string GetDefaultOpenCodePluginFilePath() => Path.Combine(GetDefaultOpenCodePluginsDirectoryPath(), ManagedPluginFileName);

    protected override string GetDefaultConfigurationFilePath() => GetDefaultOpenCodePluginFilePath();

    protected override HookInstallationInspection InspectConfiguration(HookInstallationRequest request, string hookCommand, string content, bool configurationFileExists) => OpenCodeHookPluginDocument.InspectPlugin(request.ConfigurationFilePath, request.HookExecutablePath, hookCommand, content, configurationFileExists);

    protected override bool TryCreateInstalledContent(string originalContent, string hookCommand, out string updatedContent, out string message)
    {
        updatedContent = OpenCodeHookPluginDocument.InstallManagedPlugin(hookCommand);
        message = string.Empty;
        return true;
    }

    protected override bool TryCreateRemovedContent(string originalContent, out string updatedContent, out bool changed, out string message) => OpenCodeHookPluginDocument.TryRemoveManagedPlugin(originalContent, out updatedContent, out changed, out message);
}
