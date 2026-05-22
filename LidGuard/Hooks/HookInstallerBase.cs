using LidGuard.Localization;
using LidGuard.Sessions;

namespace LidGuard.Hooks;

public abstract class HookInstallerBase : IHookInstaller
{
    protected abstract AgentProvider Provider { get; }

    protected abstract string ProviderDisplayName { get; }

    protected abstract string DefaultHookCommandName { get; }

    protected virtual string ConfigurationMissingMessage
        => Format("HookManagementConfigurationFileDoesNotExist", ProviderDisplayName);

    public HookInstallationInspection Inspect(HookInstallationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = NormalizeRequest(request);
        var hookCommand = HookCommandUtilities.CreateHookCommand(normalizedRequest.HookExecutablePath, normalizedRequest.HookCommandName);
        var configurationFileExists = File.Exists(normalizedRequest.ConfigurationFilePath);
        if (!configurationFileExists) return AddProviderSpecificInspectionDetails(normalizedRequest, CreateMissingConfigurationInspection(normalizedRequest, hookCommand));

        var content = File.ReadAllText(normalizedRequest.ConfigurationFilePath);
        return AddProviderSpecificInspectionDetails(normalizedRequest, InspectConfiguration(normalizedRequest, hookCommand, content, configurationFileExists));
    }

    public HookInstallationResult Install(HookInstallationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = NormalizeRequest(request);
        if (normalizedRequest.Provider != Provider) return HookInstallationResult.Failure(CreateUnsupportedInspection(normalizedRequest, CreateUnsupportedInstallationMessage()), CreateUnsupportedInstallationMessage());

        if (!HookCommandUtilities.HookExecutableExists(normalizedRequest.HookExecutablePath))
        {
            var missingExecutableInspection = Inspect(normalizedRequest);
            return HookInstallationResult.Failure(missingExecutableInspection, Format("HookManagementHookExecutableDoesNotExist", normalizedRequest.HookExecutablePath));
        }

        var hookCommand = HookCommandUtilities.CreateHookCommand(normalizedRequest.HookExecutablePath, normalizedRequest.HookCommandName);
        var configurationFileExists = File.Exists(normalizedRequest.ConfigurationFilePath);
        var originalContent = configurationFileExists ? File.ReadAllText(normalizedRequest.ConfigurationFilePath) : string.Empty;
        var currentInspection = Inspect(normalizedRequest);
        if (ShouldSkipInstall(currentInspection, out var skipMessage)) return HookInstallationResult.Success(currentInspection, false, skipMessage);
        if (!TryCreateInstalledContent(originalContent, hookCommand, out var updatedContent, out var updateMessage)) return HookInstallationResult.Failure(currentInspection, updateMessage);

        if (string.Equals(originalContent, updatedContent, StringComparison.Ordinal))
        {
            var unchangedInspection = Inspect(normalizedRequest);
            return HookInstallationResult.Success(unchangedInspection, false, CreateAlreadyInstalledMessage());
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
        var message = inspection.IsInstalled ? CreateInstalledMessage() : CreateWrittenNeedsAttentionMessage();
        return HookInstallationResult.Success(inspection, true, message, backupFilePath);
    }

    public HookInstallationResult Remove(HookInstallationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = NormalizeRequest(request);
        if (normalizedRequest.Provider != Provider) return HookInstallationResult.Failure(CreateUnsupportedInspection(normalizedRequest, CreateUnsupportedRemovalMessage()), CreateUnsupportedRemovalMessage());

        var configurationFileExists = File.Exists(normalizedRequest.ConfigurationFilePath);
        if (!configurationFileExists) return HookInstallationResult.Success(Inspect(normalizedRequest), false, CreateNotInstalledMessage());

        var originalContent = File.ReadAllText(normalizedRequest.ConfigurationFilePath);
        var currentInspection = Inspect(normalizedRequest);
        if (!TryCreateRemovedContent(originalContent, out var updatedContent, out var changed, out var updateMessage)) return HookInstallationResult.Failure(currentInspection, updateMessage);
        if (!changed) return HookInstallationResult.Success(currentInspection, false, CreateNoManagedHookFoundMessage());

        var backupFilePath = string.Empty;
        if (normalizedRequest.CreateBackup)
        {
            backupFilePath = HookCommandUtilities.CreateBackupFilePath(normalizedRequest.ConfigurationFilePath);
            File.Copy(normalizedRequest.ConfigurationFilePath, backupFilePath, false);
        }

        File.WriteAllText(normalizedRequest.ConfigurationFilePath, updatedContent);

        var inspection = Inspect(normalizedRequest);
        return HookInstallationResult.Success(inspection, true, CreateRemovedMessage(), backupFilePath);
    }

    public HookInstallationRequest CreateDefaultRequest(string configurationFilePath = "", bool createBackup = true)
    {
        return new HookInstallationRequest
        {
            Provider = Provider,
            ConfigurationFilePath = string.IsNullOrWhiteSpace(configurationFilePath) ? GetDefaultConfigurationFilePath() : Path.GetFullPath(configurationFilePath),
            HookExecutablePath = HookCommandUtilities.GetDefaultHookExecutableReference(),
            HookCommandName = DefaultHookCommandName,
            CreateBackup = createBackup
        };
    }

    protected abstract string GetDefaultConfigurationFilePath();

    protected abstract HookInstallationInspection InspectConfiguration(HookInstallationRequest request, string hookCommand, string content, bool configurationFileExists);

    protected abstract bool TryCreateInstalledContent(string originalContent, string hookCommand, out string updatedContent, out string message);

    protected abstract bool TryCreateRemovedContent(string originalContent, out string updatedContent, out bool changed, out string message);

    protected virtual HookInstallationInspection AddProviderSpecificInspectionDetails(HookInstallationRequest request, HookInstallationInspection inspection) => inspection;

    protected virtual bool ShouldSkipInstall(HookInstallationInspection currentInspection, out string message)
    {
        message = string.Empty;
        return false;
    }

    private HookInstallationInspection CreateMissingConfigurationInspection(HookInstallationRequest request, string hookCommand)
    {
        return new HookInstallationInspection
        {
            Provider = Provider,
            Status = HookInstallationStatus.NotInstalled,
            ConfigurationFilePath = request.ConfigurationFilePath,
            HookExecutablePath = request.HookExecutablePath,
            HookCommand = hookCommand,
            ConfigurationFileExists = false,
            Message = ConfigurationMissingMessage
        };
    }

    private HookInstallationInspection CreateUnsupportedInspection(HookInstallationRequest request, string message)
    {
        return new HookInstallationInspection
        {
            Provider = request.Provider,
            Status = HookInstallationStatus.Unknown,
            ConfigurationFilePath = request.ConfigurationFilePath,
            HookExecutablePath = request.HookExecutablePath,
            Message = message
        };
    }

    private HookInstallationRequest NormalizeRequest(HookInstallationRequest request)
    {
        return new HookInstallationRequest
        {
            Provider = request.Provider == AgentProvider.Unknown ? Provider : request.Provider,
            ConfigurationFilePath = string.IsNullOrWhiteSpace(request.ConfigurationFilePath) ? GetDefaultConfigurationFilePath() : Path.GetFullPath(request.ConfigurationFilePath),
            HookExecutablePath = string.IsNullOrWhiteSpace(request.HookExecutablePath) ? HookCommandUtilities.GetDefaultHookExecutableReference() : HookCommandUtilities.NormalizeHookExecutableReference(request.HookExecutablePath),
            HookCommandName = string.IsNullOrWhiteSpace(request.HookCommandName) ? DefaultHookCommandName : request.HookCommandName,
            CreateBackup = request.CreateBackup
        };
    }

    private string CreateAlreadyInstalledMessage()
        => Format("HookManagementAlreadyInstalled", ProviderDisplayName);

    private string CreateInstalledMessage()
        => Format("HookManagementInstalled", ProviderDisplayName);

    private string CreateWrittenNeedsAttentionMessage()
        => Format("HookManagementWrittenNeedsAttention", ProviderDisplayName);

    private string CreateNotInstalledMessage()
        => Format("HookManagementNotInstalled", ProviderDisplayName);

    private string CreateNoManagedHookFoundMessage()
        => Format("HookManagementNoManagedHookFound", ProviderDisplayName);

    private string CreateRemovedMessage()
        => Format("HookManagementRemoved", ProviderDisplayName);

    private string CreateUnsupportedInstallationMessage()
        => Format("HookManagementUnsupportedInstallation", ProviderDisplayName);

    private string CreateUnsupportedRemovalMessage()
        => Format("HookManagementUnsupportedRemoval", ProviderDisplayName);

    private static string Format(string resourceName, params object[] arguments)
        => LocalizationService.GetFormattedString(resourceName, arguments);
}
