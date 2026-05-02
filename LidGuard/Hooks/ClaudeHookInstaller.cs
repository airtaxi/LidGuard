using LidGuard.Hooks;
using LidGuard.Sessions;

namespace LidGuard.Hooks;

public sealed class ClaudeHookInstaller
{
    private const string ClaudeConfigurationDirectoryEnvironmentVariableName = "CLAUDE_CONFIG_DIR";
    private const string ClaudeConfigurationDirectoryName = ".claude";
    private const string ClaudeConfigurationFileName = "settings.json";

    public ClaudeHookInstallationInspection Inspect(ClaudeHookInstallationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = NormalizeRequest(request);
        var hookCommand = HookCommandUtilities.CreateHookCommand(normalizedRequest.HookExecutablePath, normalizedRequest.HookCommandName);
        var configurationFileExists = File.Exists(normalizedRequest.ConfigurationFilePath);
        var content = configurationFileExists ? File.ReadAllText(normalizedRequest.ConfigurationFilePath) : string.Empty;
        if (!configurationFileExists)
        {
            return new ClaudeHookInstallationInspection
            {
                Provider = AgentProvider.Claude,
                Status = CodexHookInstallationStatus.NotInstalled,
                ConfigurationFilePath = normalizedRequest.ConfigurationFilePath,
                HookExecutablePath = normalizedRequest.HookExecutablePath,
                HookCommand = hookCommand,
                ConfigurationFileExists = false,
                Message = "Claude settings file does not exist."
            };
        }

        return ClaudeHookSettingsJsonDocument.InspectSettingsJson(
            normalizedRequest.ConfigurationFilePath,
            normalizedRequest.HookExecutablePath,
            hookCommand,
            content,
            configurationFileExists);
    }

    public ClaudeHookInstallationResult Install(ClaudeHookInstallationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = NormalizeRequest(request);
        if (normalizedRequest.Provider != AgentProvider.Claude)
        {
            var unsupportedInspection = new ClaudeHookInstallationInspection
            {
                Provider = normalizedRequest.Provider,
                Status = CodexHookInstallationStatus.Unknown,
                ConfigurationFilePath = normalizedRequest.ConfigurationFilePath,
                HookExecutablePath = normalizedRequest.HookExecutablePath,
                Message = "Only Claude hook installation is implemented."
            };

            return ClaudeHookInstallationResult.Failure(unsupportedInspection, unsupportedInspection.Message);
        }

        if (!HookCommandUtilities.HookExecutableExists(normalizedRequest.HookExecutablePath))
        {
            var missingExecutableInspection = Inspect(normalizedRequest);
            return ClaudeHookInstallationResult.Failure(missingExecutableInspection, $"Hook executable or command does not exist: {normalizedRequest.HookExecutablePath}");
        }

        var hookCommand = HookCommandUtilities.CreateHookCommand(normalizedRequest.HookExecutablePath, normalizedRequest.HookCommandName);
        var configurationFileExists = File.Exists(normalizedRequest.ConfigurationFilePath);
        var originalContent = configurationFileExists ? File.ReadAllText(normalizedRequest.ConfigurationFilePath) : string.Empty;
        var currentInspection = Inspect(normalizedRequest);
        if (!ClaudeHookSettingsJsonDocument.TryInstallManagedHooks(originalContent, hookCommand, out var updatedContent, out var updateMessage))
        {
            return ClaudeHookInstallationResult.Failure(currentInspection, updateMessage);
        }

        if (string.Equals(originalContent, updatedContent, StringComparison.Ordinal))
        {
            var unchangedInspection = Inspect(normalizedRequest);
            return ClaudeHookInstallationResult.Success(unchangedInspection, false, "Claude hook is already installed.");
        }

        var configurationDirectoryPath = Path.GetDirectoryName(normalizedRequest.ConfigurationFilePath);
        if (!string.IsNullOrWhiteSpace(configurationDirectoryPath)) Directory.CreateDirectory(configurationDirectoryPath);

        var backupFilePath = string.Empty;
        if (configurationFileExists && normalizedRequest.CreateBackup)
        {
            backupFilePath = HookCommandUtilities.CreateBackupFilePath(normalizedRequest.ConfigurationFilePath);
            File.Copy(normalizedRequest.ConfigurationFilePath, backupFilePath, false);
        }

        File.WriteAllText(normalizedRequest.ConfigurationFilePath, updatedContent);

        var inspection = Inspect(normalizedRequest);
        var message = inspection.IsInstalled ? "Claude hook installed." : "Claude hook configuration was written but still needs attention.";
        return ClaudeHookInstallationResult.Success(inspection, true, message, backupFilePath);
    }

    public ClaudeHookInstallationResult Remove(ClaudeHookInstallationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = NormalizeRequest(request);
        if (normalizedRequest.Provider != AgentProvider.Claude)
        {
            var unsupportedInspection = new ClaudeHookInstallationInspection
            {
                Provider = normalizedRequest.Provider,
                Status = CodexHookInstallationStatus.Unknown,
                ConfigurationFilePath = normalizedRequest.ConfigurationFilePath,
                HookExecutablePath = normalizedRequest.HookExecutablePath,
                Message = "Only Claude hook removal is implemented."
            };

            return ClaudeHookInstallationResult.Failure(unsupportedInspection, unsupportedInspection.Message);
        }

        var configurationFileExists = File.Exists(normalizedRequest.ConfigurationFilePath);
        if (!configurationFileExists) return ClaudeHookInstallationResult.Success(Inspect(normalizedRequest), false, "Claude hook is not installed.");

        var originalContent = File.ReadAllText(normalizedRequest.ConfigurationFilePath);
        var currentInspection = Inspect(normalizedRequest);
        if (!ClaudeHookSettingsJsonDocument.TryRemoveManagedHooks(originalContent, out var updatedContent, out var changed, out var updateMessage))
        {
            return ClaudeHookInstallationResult.Failure(currentInspection, updateMessage);
        }

        if (!changed) return ClaudeHookInstallationResult.Success(currentInspection, false, "No LidGuard-managed Claude hook was found.");

        var backupFilePath = string.Empty;
        if (normalizedRequest.CreateBackup)
        {
            backupFilePath = HookCommandUtilities.CreateBackupFilePath(normalizedRequest.ConfigurationFilePath);
            File.Copy(normalizedRequest.ConfigurationFilePath, backupFilePath, false);
        }

        File.WriteAllText(normalizedRequest.ConfigurationFilePath, updatedContent);

        var inspection = Inspect(normalizedRequest);
        return ClaudeHookInstallationResult.Success(inspection, true, "Claude hook removed.", backupFilePath);
    }

    public ClaudeHookInstallationRequest CreateDefaultRequest(string configurationFilePath = "")
    {
        return new ClaudeHookInstallationRequest
        {
            Provider = AgentProvider.Claude,
            ConfigurationFilePath = string.IsNullOrWhiteSpace(configurationFilePath) ? GetDefaultClaudeConfigurationFilePath() : Path.GetFullPath(configurationFilePath),
            HookExecutablePath = HookCommandUtilities.GetDefaultHookExecutableReference(),
            HookCommandName = "claude-hook"
        };
    }

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

    private static ClaudeHookInstallationRequest NormalizeRequest(ClaudeHookInstallationRequest request)
    {
        return new ClaudeHookInstallationRequest
        {
            Provider = request.Provider,
            ConfigurationFilePath = string.IsNullOrWhiteSpace(request.ConfigurationFilePath) ? GetDefaultClaudeConfigurationFilePath() : Path.GetFullPath(request.ConfigurationFilePath),
            HookExecutablePath = string.IsNullOrWhiteSpace(request.HookExecutablePath) ? HookCommandUtilities.GetDefaultHookExecutableReference() : HookCommandUtilities.NormalizeHookExecutableReference(request.HookExecutablePath),
            HookCommandName = string.IsNullOrWhiteSpace(request.HookCommandName) ? "claude-hook" : request.HookCommandName,
            CreateBackup = request.CreateBackup
        };
    }
}
