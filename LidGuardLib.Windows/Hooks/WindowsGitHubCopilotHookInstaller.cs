using System.Text.Json;
using System.Text.Json.Nodes;
using LidGuardLib.Commons.Hooks;
using LidGuardLib.Commons.Sessions;

namespace LidGuardLib.Windows.Hooks;

public sealed class WindowsGitHubCopilotHookInstaller
{
    private const string CopilotConfigurationDirectoryEnvironmentVariableName = "COPILOT_HOME";
    private const string CopilotConfigurationDirectoryName = ".copilot";
    private const string CopilotHooksDirectoryName = "hooks";
    private const string CopilotRepositorySettingsDirectoryName = "copilot";
    private const string CopilotSettingsFileName = "settings.json";
    private const string LegacyCopilotSettingsFileName = "config.json";
    private const string ManagedConfigurationFileName = "lidguard-copilot-cli.json";
    private static readonly string[] s_supportedAgentStopEventNames =
    [
        GitHubCopilotHookEventNames.AgentStop,
        GitHubCopilotHookEventNames.PascalCaseAgentStopAlias
    ];

    public GitHubCopilotHookInstallationInspection Inspect(GitHubCopilotHookInstallationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = NormalizeRequest(request);
        var hookCommand = WindowsHookCommandUtilities.CreateHookCommand(normalizedRequest.HookExecutablePath, normalizedRequest.HookCommandName);
        var expectedHookCommands = GitHubCopilotHookConfigurationJsonDocument.CreateManagedHookCommands(hookCommand);
        var configurationFileExists = File.Exists(normalizedRequest.ConfigurationFilePath);
        var conflictingAgentStopHookSources = FindConflictingAgentStopHookSources(normalizedRequest);
        if (!configurationFileExists)
        {
            return new GitHubCopilotHookInstallationInspection
            {
                ConfigurationFileExists = false,
                ConfigurationFilePath = normalizedRequest.ConfigurationFilePath,
                HookCommand = hookCommand,
                HookExecutablePath = normalizedRequest.HookExecutablePath,
                Message = "GitHub Copilot hook configuration file does not exist.",
                Provider = AgentProvider.GitHubCopilot,
                Status = CodexHookInstallationStatus.NotInstalled
            }.WithConflictingAgentStopHookSources(conflictingAgentStopHookSources);
        }

        var content = File.ReadAllText(normalizedRequest.ConfigurationFilePath);
        return GitHubCopilotHookConfigurationJsonDocument
            .InspectConfigurationJson(
                normalizedRequest.ConfigurationFilePath,
                normalizedRequest.HookExecutablePath,
                hookCommand,
                expectedHookCommands,
                content,
                configurationFileExists)
            .WithConflictingAgentStopHookSources(conflictingAgentStopHookSources);
    }

    public GitHubCopilotHookInstallationResult Install(GitHubCopilotHookInstallationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = NormalizeRequest(request);
        if (normalizedRequest.Provider != AgentProvider.GitHubCopilot)
        {
            var unsupportedInspection = new GitHubCopilotHookInstallationInspection
            {
                ConfigurationFilePath = normalizedRequest.ConfigurationFilePath,
                HookExecutablePath = normalizedRequest.HookExecutablePath,
                Message = "Only GitHub Copilot hook installation is implemented.",
                Provider = normalizedRequest.Provider,
                Status = CodexHookInstallationStatus.Unknown
            };

            return GitHubCopilotHookInstallationResult.Failure(unsupportedInspection, unsupportedInspection.Message);
        }

        if (!WindowsHookCommandUtilities.HookExecutableExists(normalizedRequest.HookExecutablePath))
        {
            var missingExecutableInspection = Inspect(normalizedRequest);
            return GitHubCopilotHookInstallationResult.Failure(
                missingExecutableInspection,
                $"Hook executable or command does not exist: {normalizedRequest.HookExecutablePath}");
        }

        var hookCommand = WindowsHookCommandUtilities.CreateHookCommand(normalizedRequest.HookExecutablePath, normalizedRequest.HookCommandName);
        var hookCommandsByEvent = GitHubCopilotHookConfigurationJsonDocument.CreateManagedHookCommands(hookCommand);
        var configurationFileExists = File.Exists(normalizedRequest.ConfigurationFilePath);
        var originalContent = configurationFileExists ? File.ReadAllText(normalizedRequest.ConfigurationFilePath) : string.Empty;
        var currentInspection = Inspect(normalizedRequest);
        if (!GitHubCopilotHookConfigurationJsonDocument.TryInstallManagedHooks(originalContent, hookCommandsByEvent, out var updatedContent, out var updateMessage))
        {
            return GitHubCopilotHookInstallationResult.Failure(currentInspection, updateMessage);
        }

        if (string.Equals(originalContent, updatedContent, StringComparison.Ordinal))
        {
            var unchangedInspection = Inspect(normalizedRequest);
            return GitHubCopilotHookInstallationResult.Success(unchangedInspection, false, "GitHub Copilot hook is already installed.");
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
        var message = inspection.IsInstalled ? "GitHub Copilot hook installed." : "GitHub Copilot hook configuration was written but still needs attention.";
        return GitHubCopilotHookInstallationResult.Success(inspection, true, message, backupFilePath);
    }

    public GitHubCopilotHookInstallationResult Remove(GitHubCopilotHookInstallationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedRequest = NormalizeRequest(request);
        if (normalizedRequest.Provider != AgentProvider.GitHubCopilot)
        {
            var unsupportedInspection = new GitHubCopilotHookInstallationInspection
            {
                ConfigurationFilePath = normalizedRequest.ConfigurationFilePath,
                HookExecutablePath = normalizedRequest.HookExecutablePath,
                Message = "Only GitHub Copilot hook removal is implemented.",
                Provider = normalizedRequest.Provider,
                Status = CodexHookInstallationStatus.Unknown
            };

            return GitHubCopilotHookInstallationResult.Failure(unsupportedInspection, unsupportedInspection.Message);
        }

        var configurationFileExists = File.Exists(normalizedRequest.ConfigurationFilePath);
        if (!configurationFileExists) return GitHubCopilotHookInstallationResult.Success(Inspect(normalizedRequest), false, "GitHub Copilot hook is not installed.");

        var originalContent = File.ReadAllText(normalizedRequest.ConfigurationFilePath);
        var currentInspection = Inspect(normalizedRequest);
        if (!GitHubCopilotHookConfigurationJsonDocument.TryRemoveManagedHooks(originalContent, out var updatedContent, out var changed, out var updateMessage))
        {
            return GitHubCopilotHookInstallationResult.Failure(currentInspection, updateMessage);
        }

        if (!changed) return GitHubCopilotHookInstallationResult.Success(currentInspection, false, "No LidGuard-managed GitHub Copilot hook was found.");

        var backupFilePath = string.Empty;
        if (normalizedRequest.CreateBackup)
        {
            backupFilePath = WindowsHookCommandUtilities.CreateBackupFilePath(normalizedRequest.ConfigurationFilePath);
            File.Copy(normalizedRequest.ConfigurationFilePath, backupFilePath, false);
        }

        File.WriteAllText(normalizedRequest.ConfigurationFilePath, updatedContent);

        var inspection = Inspect(normalizedRequest);
        return GitHubCopilotHookInstallationResult.Success(inspection, true, "GitHub Copilot hook removed.", backupFilePath);
    }

    public GitHubCopilotHookInstallationRequest CreateDefaultRequest(string configurationFilePath = "")
    {
        return new GitHubCopilotHookInstallationRequest
        {
            ConfigurationFilePath = string.IsNullOrWhiteSpace(configurationFilePath)
                ? GetDefaultGitHubCopilotHooksConfigurationFilePath()
                : Path.GetFullPath(configurationFilePath),
            HookExecutablePath = WindowsHookCommandUtilities.GetDefaultHookExecutableReference(),
            HookCommandName = "copilot-hook",
            Provider = AgentProvider.GitHubCopilot
        };
    }

    public static string GetDefaultGitHubCopilotHooksConfigurationFilePath()
        => Path.Combine(GetDefaultGitHubCopilotConfigurationDirectoryPath(), CopilotHooksDirectoryName, ManagedConfigurationFileName);

    public static string GetDefaultGitHubCopilotConfigurationDirectoryPath() => GetCopilotConfigurationDirectoryPath();

    private static void AddConflictingAgentStopHooksFromDirectory(string directoryPath, string excludedConfigurationFilePath, ISet<string> conflictingAgentStopHookSources)
    {
        if (!Directory.Exists(directoryPath)) return;

        foreach (var hookConfigurationFilePath in Directory.GetFiles(directoryPath, "*.json", SearchOption.TopDirectoryOnly))
        {
            if (string.Equals(Path.GetFullPath(hookConfigurationFilePath), excludedConfigurationFilePath, StringComparison.OrdinalIgnoreCase)) continue;
            AddConflictingAgentStopHooksFromFile(hookConfigurationFilePath, conflictingAgentStopHookSources);
        }
    }

    private static void AddConflictingAgentStopHooksFromFile(string configurationFilePath, ISet<string> conflictingAgentStopHookSources)
    {
        if (!File.Exists(configurationFilePath)) return;

        JsonObject configurationRootObject;
        try
        {
            var configurationContent = File.ReadAllText(configurationFilePath);
            var rootNode = JsonNode.Parse(configurationContent, documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            configurationRootObject = rootNode as JsonObject;
        }
        catch (JsonException)
        {
            return;
        }

        if (configurationRootObject is null) return;
        if (!TryGetHooksObject(configurationRootObject, out var hooksObject)) return;

        foreach (var hookEventName in s_supportedAgentStopEventNames)
        {
            if (!hooksObject.TryGetPropertyValue(hookEventName, out var hookEventNode) || hookEventNode is not JsonArray hookDefinitions) continue;

            foreach (var hookDefinitionNode in hookDefinitions)
            {
                if (hookDefinitionNode is not JsonObject hookDefinitionObject) continue;
                if (IsLidGuardManagedAgentStopHook(hookDefinitionObject)) continue;

                conflictingAgentStopHookSources.Add($"{configurationFilePath}:{hookEventName}");
                break;
            }
        }
    }

    private static void AddSettingsFileCandidates(List<string> settingsFilePaths, string settingsDirectoryPath)
    {
        settingsFilePaths.Add(Path.Combine(settingsDirectoryPath, CopilotSettingsFileName));
        settingsFilePaths.Add(Path.Combine(settingsDirectoryPath, LegacyCopilotSettingsFileName));
    }

    private static IReadOnlyList<string> FindConflictingAgentStopHookSources(GitHubCopilotHookInstallationRequest request)
    {
        var conflictingAgentStopHookSources = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedConfigurationFilePath = Path.GetFullPath(request.ConfigurationFilePath);

        AddConflictingAgentStopHooksFromDirectory(
            Path.Combine(GetCopilotConfigurationDirectoryPath(), CopilotHooksDirectoryName),
            normalizedConfigurationFilePath,
            conflictingAgentStopHookSources);

        var repositoryHooksDirectoryPath = Path.Combine(Environment.CurrentDirectory, ".github", CopilotHooksDirectoryName);
        AddConflictingAgentStopHooksFromDirectory(repositoryHooksDirectoryPath, normalizedConfigurationFilePath, conflictingAgentStopHookSources);

        var settingsFilePaths = new List<string>();
        AddSettingsFileCandidates(settingsFilePaths, GetCopilotConfigurationDirectoryPath());
        AddSettingsFileCandidates(settingsFilePaths, Path.Combine(Environment.CurrentDirectory, ".github", CopilotRepositorySettingsDirectoryName));
        foreach (var settingsFilePath in settingsFilePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.Equals(Path.GetFullPath(settingsFilePath), normalizedConfigurationFilePath, StringComparison.OrdinalIgnoreCase)) continue;
            AddConflictingAgentStopHooksFromFile(settingsFilePath, conflictingAgentStopHookSources);
        }

        return [.. conflictingAgentStopHookSources];
    }

    private static string GetCommandString(JsonObject hookDefinitionObject)
    {
        var powershellCommand = GetStringProperty(hookDefinitionObject, "powershell");
        if (!string.IsNullOrWhiteSpace(powershellCommand)) return powershellCommand;
        return GetStringProperty(hookDefinitionObject, "bash");
    }

    private static string GetCopilotConfigurationDirectoryPath()
    {
        var copilotConfigurationDirectoryPath = Environment.GetEnvironmentVariable(CopilotConfigurationDirectoryEnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(copilotConfigurationDirectoryPath))
        {
            var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            copilotConfigurationDirectoryPath = Path.Combine(userProfilePath, CopilotConfigurationDirectoryName);
        }

        return Path.GetFullPath(copilotConfigurationDirectoryPath);
    }

    private static string GetStringProperty(JsonObject jsonObject, string propertyName)
    {
        var valueNode = jsonObject[propertyName];
        return valueNode is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var value) ? value : string.Empty;
    }

    private static bool IsLidGuardManagedAgentStopHook(JsonObject hookDefinitionObject)
    {
        var hookCommand = GetCommandString(hookDefinitionObject);
        if (string.IsNullOrWhiteSpace(hookCommand)) return false;
        if (!hookCommand.Contains("lidguard", StringComparison.OrdinalIgnoreCase)) return false;
        if (!hookCommand.Contains("copilot-hook", StringComparison.OrdinalIgnoreCase)) return false;
        return hookCommand.Contains($"--event {GitHubCopilotHookEventNames.AgentStop}", StringComparison.OrdinalIgnoreCase);
    }

    private static GitHubCopilotHookInstallationRequest NormalizeRequest(GitHubCopilotHookInstallationRequest request)
    {
        return new GitHubCopilotHookInstallationRequest
        {
            ConfigurationFilePath = string.IsNullOrWhiteSpace(request.ConfigurationFilePath)
                ? GetDefaultGitHubCopilotHooksConfigurationFilePath()
                : Path.GetFullPath(request.ConfigurationFilePath),
            CreateBackup = request.CreateBackup,
            HookCommandName = string.IsNullOrWhiteSpace(request.HookCommandName) ? "copilot-hook" : request.HookCommandName,
            HookExecutablePath = string.IsNullOrWhiteSpace(request.HookExecutablePath)
                ? WindowsHookCommandUtilities.GetDefaultHookExecutableReference()
                : WindowsHookCommandUtilities.NormalizeHookExecutableReference(request.HookExecutablePath),
            Provider = request.Provider
        };
    }

    private static bool TryGetHooksObject(JsonObject configurationRootObject, out JsonObject hooksObject)
    {
        hooksObject = new JsonObject();
        if (!configurationRootObject.TryGetPropertyValue("hooks", out var hooksNode) || hooksNode is not JsonObject existingHooksObject) return false;

        hooksObject = existingHooksObject;
        return true;
    }
}
