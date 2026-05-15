using LidGuard.Sessions;

namespace LidGuard.Hooks;

public sealed class ClaudeHookInstaller : HookInstallerBase
{
    private const string ClaudeConfigurationDirectoryEnvironmentVariableName = "CLAUDE_CONFIG_DIR";
    private const string ClaudeConfigurationDirectoryName = ".claude";
    private const string ClaudeConfigurationFileName = "settings.json";

    protected override AgentProvider Provider => AgentProvider.Claude;

    protected override string ProviderDisplayName => "Claude";

    protected override string DefaultHookCommandName => "claude-hook";

    protected override string ConfigurationMissingMessage => "Claude settings file does not exist.";

    public static string GetDefaultClaudeConfigurationDirectoryPath()
    {
        var claudeConfigurationDirectoryPath = Environment.GetEnvironmentVariable(ClaudeConfigurationDirectoryEnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(claudeConfigurationDirectoryPath))
        {
            var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            claudeConfigurationDirectoryPath = Path.Combine(userProfilePath, ClaudeConfigurationDirectoryName);
        }

        return Path.GetFullPath(claudeConfigurationDirectoryPath);
    }

    public static string GetDefaultClaudeConfigurationFilePath()
        => Path.Combine(GetDefaultClaudeConfigurationDirectoryPath(), ClaudeConfigurationFileName);

    protected override string GetDefaultConfigurationFilePath() => GetDefaultClaudeConfigurationFilePath();

    protected override HookInstallationInspection InspectConfiguration(HookInstallationRequest request, string hookCommand, string content, bool configurationFileExists)
        => ClaudeHookSettingsJsonDocument.InspectSettingsJson(
            request.ConfigurationFilePath,
            request.HookExecutablePath,
            hookCommand,
            content,
            configurationFileExists);

    protected override bool TryCreateInstalledContent(string originalContent, string hookCommand, out string updatedContent, out string message)
        => ClaudeHookSettingsJsonDocument.TryInstallManagedHooks(originalContent, hookCommand, out updatedContent, out message);

    protected override bool TryCreateRemovedContent(string originalContent, out string updatedContent, out bool changed, out string message)
        => ClaudeHookSettingsJsonDocument.TryRemoveManagedHooks(originalContent, out updatedContent, out changed, out message);
}
