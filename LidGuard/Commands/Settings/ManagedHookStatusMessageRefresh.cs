using LidGuard.Hooks;
using LidGuard.Localization;
using LidGuard.Sessions;

namespace LidGuard.Commands;

public sealed class ManagedHookStatusMessageRefreshResult
{
    public IReadOnlyList<string> ChangedProviderNames { get; init; } = [];

    public IReadOnlyList<string> WarningMessages { get; init; } = [];
}

internal static class ManagedHookStatusMessageRefresh
{
    private delegate bool TryRefreshManagedHookConfiguration(string content, out string updatedContent, out bool changed, out string message);

    public static ManagedHookStatusMessageRefreshResult RefreshInstalledManagedHooks()
    {
        var changedProviderNames = new List<string>();
        var warningMessages = new List<string>();

        RefreshCurrentPlatformHooks(changedProviderNames, warningMessages);
#if LIDGUARD_WINDOWS
        RefreshWslHooks(changedProviderNames, warningMessages);
#endif

        return new ManagedHookStatusMessageRefreshResult
        {
            ChangedProviderNames = changedProviderNames,
            WarningMessages = warningMessages
        };
    }

    private static void RefreshCurrentPlatformHooks(List<string> changedProviderNames, List<string> warningMessages)
    {
        RefreshCurrentPlatformHook(AgentProvider.Codex, new CodexHookInstaller(), changedProviderNames, warningMessages);
        RefreshCurrentPlatformHook(AgentProvider.Claude, new ClaudeHookInstaller(), changedProviderNames, warningMessages);
        RefreshCurrentPlatformHook(AgentProvider.GitHubCopilot, new GitHubCopilotHookInstaller(), changedProviderNames, warningMessages);
    }

    private static void RefreshCurrentPlatformHook(AgentProvider provider, IHookInstaller installer, List<string> changedProviderNames, List<string> warningMessages)
    {
        var request = installer.CreateDefaultRequest(createBackup: false);
        var providerDisplayName = ManagedProviderSelection.GetProviderDisplayName(provider);
        if (!File.Exists(request.ConfigurationFilePath))
        {
            if (ShouldWarnMissingLocalConfigurationFile(request.ConfigurationFilePath)) warningMessages.Add($"{providerDisplayName}: {CreateMissingConfigurationWarning(provider)}");
            return;
        }

        var hookCommand = HookCommandUtilities.CreateHookCommand(request.HookExecutablePath, request.HookCommandName);
        RefreshConfigurationFile(providerDisplayName, request.ConfigurationFilePath, CreateLocalRefreshDelegate(provider, hookCommand), changedProviderNames, warningMessages);
    }

#if LIDGUARD_WINDOWS
    private static void RefreshWslHooks(List<string> changedProviderNames, List<string> warningMessages)
    {
        if (!WslCommandUtilities.TryListDistros(out var distroNames, out var listMessage))
        {
            warningMessages.Add($"WSL: {listMessage}");
            return;
        }

        foreach (var distroName in distroNames)
        {
            var distroDisplayName = WslCommandUtilities.GetDistroDisplayName(distroName);
            if (!WslCommandUtilities.TryValidateWsl(distroName, out var validationMessage))
            {
                warningMessages.Add($"WSL {distroDisplayName}: {validationMessage}");
                continue;
            }

            if (!WslCommandUtilities.TryGetWslLidGuardExecutablePath(distroName, out var wslExecutablePath, out var executableMessage))
            {
                warningMessages.Add($"WSL {distroDisplayName}: {executableMessage}");
                continue;
            }

            RefreshWslHookForProvider(distroName, wslExecutablePath, AgentProvider.Codex, changedProviderNames, warningMessages);
            RefreshWslHookForProvider(distroName, wslExecutablePath, AgentProvider.Claude, changedProviderNames, warningMessages);
            RefreshWslHookForProvider(distroName, wslExecutablePath, AgentProvider.GitHubCopilot, changedProviderNames, warningMessages);
        }
    }

    private static void RefreshWslHookForProvider(
        string distroName,
        string wslExecutablePath,
        AgentProvider provider,
        List<string> changedProviderNames,
        List<string> warningMessages)
    {
        var providerDisplayName = $"{ManagedProviderSelection.GetProviderDisplayName(provider)} (WSL {WslCommandUtilities.GetDistroDisplayName(distroName)})";
        if (!WslProviderConfigurationRoots.TryGetHookConfigurationFilePath(distroName, provider, string.Empty, out var configurationFilePath, out var configurationMessage))
        {
            warningMessages.Add($"{providerDisplayName}: {configurationMessage}");
            return;
        }

        if (!WslCommandUtilities.FileExists(distroName, configurationFilePath))
        {
            if (WslManagedProviderSelection.TryHasHookProviderConfigurationRoot(distroName, provider)) warningMessages.Add($"{providerDisplayName}: {CreateMissingConfigurationWarning(provider)}");
            return;
        }

        if (!WslCommandUtilities.TryReadTextFile(distroName, configurationFilePath, out var originalContent, out var readMessage))
        {
            warningMessages.Add($"{providerDisplayName}: {readMessage}");
            return;
        }

        var hookCommandName = WslCommandUtilities.GetHookCommandName(provider);
        var hookCommand = WslCommandUtilities.CreateWslLidGuardCommand(wslExecutablePath, hookCommandName);
        var refreshDelegate = CreateWslRefreshDelegate(provider, hookCommand);
        if (!refreshDelegate(originalContent, out var updatedContent, out var changed, out var refreshMessage))
        {
            warningMessages.Add($"{providerDisplayName}: {refreshMessage}");
            return;
        }

        if (!changed) return;
        if (!WslCommandUtilities.TryWriteTextFile(distroName, configurationFilePath, updatedContent, out var writeMessage))
        {
            warningMessages.Add($"{providerDisplayName}: {writeMessage}");
            return;
        }

        changedProviderNames.Add(providerDisplayName);
    }
#endif

    private static TryRefreshManagedHookConfiguration CreateLocalRefreshDelegate(AgentProvider provider, string hookCommand)
    {
        return provider switch
        {
            AgentProvider.Codex => (string content, out string updatedContent, out bool changed, out string message)
                => CodexHookConfigTomlDocument.TryRefreshManagedHookConfiguration(content, hookCommand, true, out updatedContent, out changed, out message),
            AgentProvider.Claude => (string content, out string updatedContent, out bool changed, out string message)
                => ClaudeHookSettingsJsonDocument.TryRefreshManagedHooks(content, hookCommand, HookCommandUtilities.GetCommandHookShellNameForCurrentPlatform(), true, out updatedContent, out changed, out message),
            AgentProvider.GitHubCopilot => (string content, out string updatedContent, out bool changed, out string message)
                => GitHubCopilotHookConfigurationJsonDocument.TryRefreshManagedHooks(content, GitHubCopilotHookConfigurationJsonDocument.CreateManagedHookCommands(hookCommand), HookCommandUtilities.GetCommandHookShellNameForCurrentPlatform(), true, out updatedContent, out changed, out message),
            _ => UnsupportedRefreshDelegate()
        };
    }

#if LIDGUARD_WINDOWS
    private static TryRefreshManagedHookConfiguration CreateWslRefreshDelegate(AgentProvider provider, string hookCommand)
    {
        return provider switch
        {
            AgentProvider.Codex => (string content, out string updatedContent, out bool changed, out string message)
                => CodexHookConfigTomlDocument.TryRefreshManagedHookConfiguration(content, hookCommand, true, out updatedContent, out changed, out message),
            AgentProvider.Claude => (string content, out string updatedContent, out bool changed, out string message)
                => ClaudeHookSettingsJsonDocument.TryRefreshManagedHooks(
                    content,
                    hookCommand,
                    HookCommandUtilities.BashShellName,
                    true,
                    out updatedContent,
                    out changed,
                    out message),
            AgentProvider.GitHubCopilot => (string content, out string updatedContent, out bool changed, out string message)
                => GitHubCopilotHookConfigurationJsonDocument.TryRefreshManagedHooks(
                    content,
                    GitHubCopilotHookConfigurationJsonDocument.CreateManagedHookCommands(hookCommand),
                    HookCommandUtilities.BashShellName,
                    true,
                    out updatedContent,
                    out changed,
                    out message),
            _ => UnsupportedRefreshDelegate()
        };
    }
#endif

    private static TryRefreshManagedHookConfiguration UnsupportedRefreshDelegate() => (string content, out string updatedContent, out bool changed, out string message) =>
        {
            updatedContent = content;
            changed = false;
            message = LocalizationService.GetString("ManagementUnsupportedHookManagement");
            return false;
        };

    private static void RefreshConfigurationFile(string providerDisplayName, string configurationFilePath, TryRefreshManagedHookConfiguration tryRefreshManagedHookConfiguration, List<string> changedProviderNames, List<string> warningMessages)
    {
        try
        {
            var originalContent = File.ReadAllText(configurationFilePath);
            if (!tryRefreshManagedHookConfiguration(originalContent, out var updatedContent, out var changed, out var message))
            {
                warningMessages.Add($"{providerDisplayName}: {message}");
                return;
            }

            if (!changed) return;

            File.WriteAllText(configurationFilePath, updatedContent);
            changedProviderNames.Add(providerDisplayName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException) { warningMessages.Add($"{providerDisplayName}: {exception.Message}"); }
    }

    private static string CreateMissingConfigurationWarning(AgentProvider provider) => $"{ManagedProviderSelection.GetProviderDisplayName(provider)} hook configuration file does not exist.";

    private static bool ShouldWarnMissingLocalConfigurationFile(string configurationFilePath)
    {
        var configurationDirectoryPath = Path.GetDirectoryName(configurationFilePath);
        return !string.IsNullOrWhiteSpace(configurationDirectoryPath) && Directory.Exists(configurationDirectoryPath);
    }
}
