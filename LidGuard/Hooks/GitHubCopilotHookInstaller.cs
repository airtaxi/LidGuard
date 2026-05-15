using System.Text.Json;
using System.Text.Json.Nodes;
using LidGuard.Sessions;

namespace LidGuard.Hooks;

public sealed class GitHubCopilotHookInstaller : HookInstallerBase
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

    protected override AgentProvider Provider => AgentProvider.GitHubCopilot;

    protected override string ProviderDisplayName => "GitHub Copilot";

    protected override string DefaultHookCommandName => "copilot-hook";

    public static string GetDefaultGitHubCopilotHooksConfigurationFilePath()
        => Path.Combine(GetDefaultGitHubCopilotConfigurationDirectoryPath(), CopilotHooksDirectoryName, ManagedConfigurationFileName);

    public static string GetDefaultGitHubCopilotConfigurationDirectoryPath() => GetCopilotConfigurationDirectoryPath();

    protected override string GetDefaultConfigurationFilePath() => GetDefaultGitHubCopilotHooksConfigurationFilePath();

    protected override HookInstallationInspection InspectConfiguration(HookInstallationRequest request, string hookCommand, string content, bool configurationFileExists)
    {
        var expectedHookCommands = GitHubCopilotHookConfigurationJsonDocument.CreateManagedHookCommands(hookCommand);
        return GitHubCopilotHookConfigurationJsonDocument.InspectConfigurationJson(
            request.ConfigurationFilePath,
            request.HookExecutablePath,
            hookCommand,
            expectedHookCommands,
            content,
            configurationFileExists);
    }

    protected override bool TryCreateInstalledContent(string originalContent, string hookCommand, out string updatedContent, out string message)
    {
        var hookCommandsByEvent = GitHubCopilotHookConfigurationJsonDocument.CreateManagedHookCommands(hookCommand);
        return GitHubCopilotHookConfigurationJsonDocument.TryInstallManagedHooks(originalContent, hookCommandsByEvent, out updatedContent, out message);
    }

    protected override bool TryCreateRemovedContent(string originalContent, out string updatedContent, out bool changed, out string message)
        => GitHubCopilotHookConfigurationJsonDocument.TryRemoveManagedHooks(originalContent, out updatedContent, out changed, out message);

    protected override HookInstallationInspection AddProviderSpecificInspectionDetails(HookInstallationRequest request, HookInstallationInspection inspection)
        => inspection.WithConflictingAgentStopHookSources(FindConflictingAgentStopHookSources(request));

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
        catch (JsonException) { return; }

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

    private static IReadOnlyList<string> FindConflictingAgentStopHookSources(HookInstallationRequest request)
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
        var currentPlatformShellName = HookCommandUtilities.GetCommandHookShellNameForCurrentPlatform();
        var currentPlatformCommand = GetStringProperty(hookDefinitionObject, currentPlatformShellName);
        if (!string.IsNullOrWhiteSpace(currentPlatformCommand)) return currentPlatformCommand;

        var alternatePlatformShellName = currentPlatformShellName.Equals("powershell", StringComparison.Ordinal)
            ? "bash"
            : "powershell";
        var alternatePlatformCommand = GetStringProperty(hookDefinitionObject, alternatePlatformShellName);
        if (!string.IsNullOrWhiteSpace(alternatePlatformCommand)) return alternatePlatformCommand;

        return GetStringProperty(hookDefinitionObject, "command");
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
        => HookJsonPropertyReader.GetStringProperty(jsonObject, propertyName);

    private static bool IsLidGuardManagedAgentStopHook(JsonObject hookDefinitionObject)
    {
        var hookCommand = GetCommandString(hookDefinitionObject);
        if (string.IsNullOrWhiteSpace(hookCommand)) return false;
        if (!hookCommand.Contains("lidguard", StringComparison.OrdinalIgnoreCase)) return false;
        if (!hookCommand.Contains("copilot-hook", StringComparison.OrdinalIgnoreCase)) return false;
        return hookCommand.Contains($"--event {GitHubCopilotHookEventNames.AgentStop}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetHooksObject(JsonObject configurationRootObject, out JsonObject hooksObject)
    {
        hooksObject = new JsonObject();
        if (!configurationRootObject.TryGetPropertyValue("hooks", out var hooksNode) || hooksNode is not JsonObject existingHooksObject) return false;

        hooksObject = existingHooksObject;
        return true;
    }
}
