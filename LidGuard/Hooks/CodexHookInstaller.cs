using LidGuard.Localization;
using LidGuard.Sessions;

namespace LidGuard.Hooks;

public sealed class CodexHookInstaller : HookInstallerBase
{
    private const string CodexConfigurationDirectoryName = ".codex";
    private const string CodexConfigurationFileName = "config.toml";

    protected override AgentProvider Provider => AgentProvider.Codex;

    protected override string ProviderDisplayName => "Codex";

    protected override string DefaultHookCommandName => "codex-hook";

    public static string GetDefaultCodexConfigurationDirectoryPath()
    {
        var codexHomePath = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (string.IsNullOrWhiteSpace(codexHomePath))
        {
            var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            codexHomePath = Path.Combine(userProfilePath, CodexConfigurationDirectoryName);
        }

        return Path.GetFullPath(codexHomePath);
    }

    public static string GetDefaultCodexConfigurationFilePath()
        => Path.Combine(GetDefaultCodexConfigurationDirectoryPath(), CodexConfigurationFileName);

    protected override string GetDefaultConfigurationFilePath() => GetDefaultCodexConfigurationFilePath();

    protected override HookInstallationInspection InspectConfiguration(HookInstallationRequest request, string hookCommand, string content, bool configurationFileExists)
        => CodexHookConfigTomlDocument.InspectConfigToml(
            request.ConfigurationFilePath,
            request.HookExecutablePath,
            hookCommand,
            content,
            configurationFileExists);

    protected override bool TryCreateInstalledContent(string originalContent, string hookCommand, out string updatedContent, out string message)
    {
        updatedContent = CodexHookConfigTomlDocument.InstallManagedHookBlock(originalContent, hookCommand);
        message = string.Empty;
        return true;
    }

    protected override bool TryCreateRemovedContent(string originalContent, out string updatedContent, out bool changed, out string message)
    {
        updatedContent = CodexHookConfigTomlDocument.RemoveManagedHookBlock(originalContent);
        changed = !string.Equals(originalContent, updatedContent, StringComparison.Ordinal);
        message = string.Empty;
        return true;
    }

    protected override bool ShouldSkipInstall(HookInstallationInspection currentInspection, out string message)
    {
        message = LocalizationService.GetFormattedStringWithFallback(
            "HookManagementAlreadyInstalledOutsideManagedBlock",
            "{0} hook is already installed outside the LidGuard managed block.",
            ProviderDisplayName);
        return currentInspection.IsInstalled && !currentInspection.HasCheck(HookInstallationCheck.ManagedBlock);
    }
}
