using LidGuardLib.Commons.Hooks;
using LidGuardLib.Commons.Sessions;

namespace LidGuardLib.Windows.Hooks;

public sealed class WindowsClaudeHookInstaller
{
    private const string ClaudeConfigurationDirectoryEnvironmentVariableName = "CLAUDE_CONFIG_DIR";
    private const string ClaudeConfigurationDirectoryName = ".claude";
    private const string ClaudeConfigurationFileName = "settings.json";

    public ClaudeHookInstallationInspection Inspect(ClaudeHookInstallationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = NormalizeRequest(request);
        var hookCommand = WindowsHookCommandUtilities.CreateHookCommand(normalizedRequest.HookExecutablePath, normalizedRequest.HookCommandName);
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

        if (!WindowsHookCommandUtilities.HookExecutableExists(normalizedRequest.HookExecutablePath))
        {
            var missingExecutableInspection = Inspect(normalizedRequest);
            return ClaudeHookInstallationResult.Failure(missingExecutableInspection, $"Hook executable or command does not exist: {normalizedRequest.HookExecutablePath}");
        }

        var hookCommand = WindowsHookCommandUtilities.CreateHookCommand(normalizedRequest.HookExecutablePath, normalizedRequest.HookCommandName);
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
            backupFilePath = WindowsHookCommandUtilities.CreateBackupFilePath(normalizedRequest.ConfigurationFilePath);
            File.Copy(normalizedRequest.ConfigurationFilePath, backupFilePath, false);
        }

        File.WriteAllText(normalizedRequest.ConfigurationFilePath, updatedContent);

        var inspection = Inspect(normalizedRequest);
        var message = inspection.IsInstalled ? "Claude hook installed." : "Claude hook configuration was written but still needs attention.";
        return ClaudeHookInstallationResult.Success(inspection, true, message, backupFilePath);
    }

    public ClaudeHookInstallationRequest CreateDefaultRequest(string hookExecutablePath = "", string configurationFilePath = "")
    {
        return new ClaudeHookInstallationRequest
        {
            Provider = AgentProvider.Claude,
            ConfigurationFilePath = string.IsNullOrWhiteSpace(configurationFilePath) ? GetDefaultClaudeConfigurationFilePath() : Path.GetFullPath(configurationFilePath),
            HookExecutablePath = string.IsNullOrWhiteSpace(hookExecutablePath) ? WindowsHookCommandUtilities.GetDefaultHookExecutableReference() : WindowsHookCommandUtilities.NormalizeHookExecutableReference(hookExecutablePath),
            HookCommandName = "claude-hook"
        };
    }

    public static string GetDefaultClaudeConfigurationFilePath()
    {
        var claudeConfigurationDirectoryPath = Environment.GetEnvironmentVariable(ClaudeConfigurationDirectoryEnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(claudeConfigurationDirectoryPath))
        {
            var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            claudeConfigurationDirectoryPath = Path.Combine(userProfilePath, ClaudeConfigurationDirectoryName);
        }

        return Path.Combine(claudeConfigurationDirectoryPath, ClaudeConfigurationFileName);
    }

    private static ClaudeHookInstallationRequest NormalizeRequest(ClaudeHookInstallationRequest request)
    {
        return new ClaudeHookInstallationRequest
        {
            Provider = request.Provider,
            ConfigurationFilePath = string.IsNullOrWhiteSpace(request.ConfigurationFilePath) ? GetDefaultClaudeConfigurationFilePath() : Path.GetFullPath(request.ConfigurationFilePath),
            HookExecutablePath = string.IsNullOrWhiteSpace(request.HookExecutablePath) ? WindowsHookCommandUtilities.GetDefaultHookExecutableReference() : WindowsHookCommandUtilities.NormalizeHookExecutableReference(request.HookExecutablePath),
            HookCommandName = string.IsNullOrWhiteSpace(request.HookCommandName) ? "claude-hook" : request.HookCommandName,
            CreateBackup = request.CreateBackup
        };
    }
}
