using LidGuard.Hooks;
using LidGuard.Sessions;

namespace LidGuard.Commands;

internal sealed class ManagedHookStatusMessageRefreshResult
{
    public IReadOnlyList<string> ChangedProviderNames { get; init; } = [];

    public IReadOnlyList<string> WarningMessages { get; init; } = [];
}

internal static class ManagedHookStatusMessageRefresh
{
    private delegate bool TryRefreshManagedHookStatusMessages(
        string content,
        out string updatedContent,
        out bool changed,
        out string message);

    public static ManagedHookStatusMessageRefreshResult RefreshInstalledManagedHooks()
    {
        var changedProviderNames = new List<string>();
        var warningMessages = new List<string>();

        RefreshCodexHookStatusMessages(changedProviderNames, warningMessages);
        RefreshClaudeHookStatusMessages(changedProviderNames, warningMessages);
        RefreshGitHubCopilotHookStatusMessages(changedProviderNames, warningMessages);

        return new ManagedHookStatusMessageRefreshResult
        {
            ChangedProviderNames = changedProviderNames,
            WarningMessages = warningMessages
        };
    }

    private static void RefreshCodexHookStatusMessages(List<string> changedProviderNames, List<string> warningMessages)
    {
        var installer = new CodexHookInstaller();
        var request = installer.CreateDefaultRequest(createBackup: false);
        RefreshConfigurationFile(
            ManagedProviderSelection.GetProviderDisplayName(AgentProvider.Codex),
            request.ConfigurationFilePath,
            CodexHookConfigTomlDocument.TryRefreshManagedHookStatusMessages,
            changedProviderNames,
            warningMessages);
    }

    private static void RefreshClaudeHookStatusMessages(List<string> changedProviderNames, List<string> warningMessages)
    {
        var installer = new ClaudeHookInstaller();
        var request = installer.CreateDefaultRequest(createBackup: false);
        RefreshConfigurationFile(
            ManagedProviderSelection.GetProviderDisplayName(AgentProvider.Claude),
            request.ConfigurationFilePath,
            ClaudeHookSettingsJsonDocument.TryRefreshManagedHookStatusMessages,
            changedProviderNames,
            warningMessages);
    }

    private static void RefreshGitHubCopilotHookStatusMessages(List<string> changedProviderNames, List<string> warningMessages)
    {
        var installer = new GitHubCopilotHookInstaller();
        var request = installer.CreateDefaultRequest(createBackup: false);
        RefreshConfigurationFile(
            ManagedProviderSelection.GetProviderDisplayName(AgentProvider.GitHubCopilot),
            request.ConfigurationFilePath,
            GitHubCopilotHookConfigurationJsonDocument.TryRefreshManagedHookStatusMessages,
            changedProviderNames,
            warningMessages);
    }

    private static void RefreshConfigurationFile(
        string providerName,
        string configurationFilePath,
        TryRefreshManagedHookStatusMessages tryRefreshManagedHookStatusMessages,
        List<string> changedProviderNames,
        List<string> warningMessages)
    {
        if (!File.Exists(configurationFilePath)) return;

        try
        {
            var originalContent = File.ReadAllText(configurationFilePath);
            if (!tryRefreshManagedHookStatusMessages(originalContent, out var updatedContent, out var changed, out var message))
            {
                warningMessages.Add($"{providerName}: {message}");
                return;
            }

            if (!changed) return;

            File.WriteAllText(configurationFilePath, updatedContent);
            changedProviderNames.Add(providerName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            warningMessages.Add($"{providerName}: {exception.Message}");
        }
    }
}
