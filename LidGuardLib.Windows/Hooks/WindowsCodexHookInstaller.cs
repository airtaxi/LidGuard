using LidGuardLib.Commons.Hooks;
using LidGuardLib.Commons.Sessions;

namespace LidGuardLib.Windows.Hooks;

public sealed class WindowsCodexHookInstaller
{
    private const string CodexConfigurationDirectoryName = ".codex";
    private const string CodexConfigurationFileName = "config.toml";

    public CodexHookInstallationInspection Inspect(CodexHookInstallationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = NormalizeRequest(request);
        var hookCommand = WindowsHookCommandUtilities.CreateHookCommand(normalizedRequest.HookExecutablePath, normalizedRequest.HookCommandName);
        var configurationFileExists = File.Exists(normalizedRequest.ConfigurationFilePath);
        var content = configurationFileExists ? File.ReadAllText(normalizedRequest.ConfigurationFilePath) : string.Empty;
        if (!configurationFileExists)
        {
            return new CodexHookInstallationInspection
            {
                Provider = AgentProvider.Codex,
                Format = CodexHookConfigurationFormat.ConfigToml,
                Status = CodexHookInstallationStatus.NotInstalled,
                ConfigurationFilePath = normalizedRequest.ConfigurationFilePath,
                HookExecutablePath = normalizedRequest.HookExecutablePath,
                HookCommand = hookCommand,
                ConfigurationFileExists = false,
                Message = "Codex configuration file does not exist."
            };
        }

        return CodexHookConfigTomlDocument.InspectConfigToml(
            normalizedRequest.ConfigurationFilePath,
            normalizedRequest.HookExecutablePath,
            hookCommand,
            content,
            configurationFileExists);
    }

    public CodexHookInstallationResult Install(CodexHookInstallationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = NormalizeRequest(request);
        if (normalizedRequest.Provider != AgentProvider.Codex)
        {
            var unsupportedInspection = new CodexHookInstallationInspection
            {
                Provider = normalizedRequest.Provider,
                Format = normalizedRequest.Format,
                Status = CodexHookInstallationStatus.Unknown,
                ConfigurationFilePath = normalizedRequest.ConfigurationFilePath,
                HookExecutablePath = normalizedRequest.HookExecutablePath,
                Message = "Only Codex hook installation is implemented."
            };

            return CodexHookInstallationResult.Failure(unsupportedInspection, unsupportedInspection.Message);
        }

        if (!HookExecutableExists(normalizedRequest.HookExecutablePath))
        {
            var missingExecutableInspection = Inspect(normalizedRequest);
            return CodexHookInstallationResult.Failure(missingExecutableInspection, $"Hook executable or command does not exist: {normalizedRequest.HookExecutablePath}");
        }

        var hookCommand = WindowsHookCommandUtilities.CreateHookCommand(normalizedRequest.HookExecutablePath, normalizedRequest.HookCommandName);
        var configurationFileExists = File.Exists(normalizedRequest.ConfigurationFilePath);
        var originalContent = configurationFileExists ? File.ReadAllText(normalizedRequest.ConfigurationFilePath) : string.Empty;
        var currentInspection = configurationFileExists
            ? CodexHookConfigTomlDocument.InspectConfigToml(
                normalizedRequest.ConfigurationFilePath,
                normalizedRequest.HookExecutablePath,
                hookCommand,
                originalContent,
                true)
            : Inspect(normalizedRequest);

        if (currentInspection.IsInstalled && !currentInspection.HasManagedBlock) return CodexHookInstallationResult.Success(currentInspection, false, "Codex hook is already installed outside the LidGuard managed block.");

        var updatedContent = CodexHookConfigTomlDocument.InstallManagedHookBlock(originalContent, hookCommand);

        if (string.Equals(originalContent, updatedContent, StringComparison.Ordinal))
        {
            var unchangedInspection = Inspect(normalizedRequest);
            return CodexHookInstallationResult.Success(unchangedInspection, false, "Codex hook is already installed.");
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
        var message = inspection.IsInstalled ? "Codex hook installed." : "Codex hook configuration was written but still needs attention.";
        return CodexHookInstallationResult.Success(inspection, true, message, backupFilePath);
    }

    public CodexHookInstallationResult Remove(CodexHookInstallationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = NormalizeRequest(request);
        if (normalizedRequest.Provider != AgentProvider.Codex)
        {
            var unsupportedInspection = new CodexHookInstallationInspection
            {
                Provider = normalizedRequest.Provider,
                Format = normalizedRequest.Format,
                Status = CodexHookInstallationStatus.Unknown,
                ConfigurationFilePath = normalizedRequest.ConfigurationFilePath,
                HookExecutablePath = normalizedRequest.HookExecutablePath,
                Message = "Only Codex hook removal is implemented."
            };

            return CodexHookInstallationResult.Failure(unsupportedInspection, unsupportedInspection.Message);
        }

        var configurationFileExists = File.Exists(normalizedRequest.ConfigurationFilePath);
        if (!configurationFileExists) return CodexHookInstallationResult.Success(Inspect(normalizedRequest), false, "Codex hook is not installed.");

        var originalContent = File.ReadAllText(normalizedRequest.ConfigurationFilePath);
        var updatedContent = CodexHookConfigTomlDocument.RemoveManagedHookBlock(originalContent);
        if (string.Equals(originalContent, updatedContent, StringComparison.Ordinal)) return CodexHookInstallationResult.Success(Inspect(normalizedRequest), false, "No LidGuard-managed Codex hook was found.");

        var backupFilePath = string.Empty;
        if (normalizedRequest.CreateBackup)
        {
            backupFilePath = WindowsHookCommandUtilities.CreateBackupFilePath(normalizedRequest.ConfigurationFilePath);
            File.Copy(normalizedRequest.ConfigurationFilePath, backupFilePath, false);
        }

        File.WriteAllText(normalizedRequest.ConfigurationFilePath, updatedContent);

        var inspection = Inspect(normalizedRequest);
        return CodexHookInstallationResult.Success(inspection, true, "Codex hook removed.", backupFilePath);
    }

    public CodexHookInstallationRequest CreateDefaultRequest(string configurationFilePath = "")
    {
        return new CodexHookInstallationRequest
        {
            Provider = AgentProvider.Codex,
            Format = CodexHookConfigurationFormat.ConfigToml,
            ConfigurationFilePath = string.IsNullOrWhiteSpace(configurationFilePath) ? GetDefaultCodexConfigurationFilePath() : Path.GetFullPath(configurationFilePath),
            HookExecutablePath = WindowsHookCommandUtilities.GetDefaultHookExecutableReference(),
            HookCommandName = "codex-hook"
        };
    }

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

    private static CodexHookInstallationRequest NormalizeRequest(CodexHookInstallationRequest request)
    {
        return new CodexHookInstallationRequest
        {
            Provider = request.Provider,
            Format = request.Format,
            ConfigurationFilePath = string.IsNullOrWhiteSpace(request.ConfigurationFilePath) ? GetDefaultCodexConfigurationFilePath() : Path.GetFullPath(request.ConfigurationFilePath),
            HookExecutablePath = string.IsNullOrWhiteSpace(request.HookExecutablePath) ? WindowsHookCommandUtilities.GetDefaultHookExecutableReference() : WindowsHookCommandUtilities.NormalizeHookExecutableReference(request.HookExecutablePath),
            HookCommandName = string.IsNullOrWhiteSpace(request.HookCommandName) ? "codex-hook" : request.HookCommandName,
            CreateBackup = request.CreateBackup
        };
    }

    private static bool HookExecutableExists(string executableReference) => WindowsHookCommandUtilities.HookExecutableExists(executableReference);
}
