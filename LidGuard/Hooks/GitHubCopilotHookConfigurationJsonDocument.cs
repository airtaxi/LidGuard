using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using LidGuard.Localization;
using LidGuard.Sessions;

namespace LidGuard.Hooks;

public static class GitHubCopilotHookConfigurationJsonDocument
{
    private const string CommandHookTypeName = "command";
    private const string HooksPropertyName = JsonHookConfigurationDocument.HooksPropertyName;
    private const int SupportedSchemaVersion = 1;
    private const string VersionPropertyName = "version";
    private static readonly JsonSerializerOptions s_jsonSerializerOptions = new() { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All), WriteIndented = true };
    private static readonly (string HookEventName, Func<string> GetStatusMessage, string Matcher)[] s_requiredHookDefinitions =
    [
        (GitHubCopilotHookEventNames.SessionStart, () => LocalizationService.GetString("HookStatusMessageRecordingGitHubCopilotSessionStart"), string.Empty),
        (GitHubCopilotHookEventNames.SessionEnd, () => LocalizationService.GetString("HookStatusMessageRecordingGitHubCopilotSessionEnd"), string.Empty),
        (GitHubCopilotHookEventNames.UserPromptSubmitted, () => LocalizationService.GetString("HookStatusMessageStartingTurnProtection"), string.Empty),
        (GitHubCopilotHookEventNames.PreToolUse, () => LocalizationService.GetString("HookStatusMessageBlockingClosedLidAskUserPrompt"), string.Empty),
        (GitHubCopilotHookEventNames.PostToolUse, () => LocalizationService.GetString("HookStatusMessageRecordingGitHubCopilotToolCompletionActivity"), string.Empty),
        (GitHubCopilotHookEventNames.PermissionRequest, () => LocalizationService.GetString("HookStatusMessageRespondingToClosedLidPermissionRequest"), string.Empty),
        (GitHubCopilotHookEventNames.AgentStop, () => LocalizationService.GetString("HookStatusMessageStoppingTurnProtection"), string.Empty),
        (GitHubCopilotHookEventNames.SubagentStart, () => LocalizationService.GetString("HookStatusMessageRecordingGitHubCopilotSubagentActivity"), string.Empty),
        (GitHubCopilotHookEventNames.SubagentStop, () => LocalizationService.GetString("HookStatusMessageRecordingGitHubCopilotSubagentCompletionActivity"), string.Empty),
        (GitHubCopilotHookEventNames.ErrorOccurred, () => LocalizationService.GetString("HookStatusMessageRecordingGitHubCopilotErrorTelemetry"), string.Empty),
        (GitHubCopilotHookEventNames.Notification, () => LocalizationService.GetString("HookStatusMessageRecordingGitHubCopilotPromptTelemetry"), GitHubCopilotSoftLockSignalSource.NotificationMatcher)
    ];

    public static IReadOnlyDictionary<string, string> CreateManagedHookCommands(string hookCommand)
    {
        var hookCommands = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var hookDefinition in s_requiredHookDefinitions) hookCommands[hookDefinition.HookEventName] = $"{hookCommand} --event {hookDefinition.HookEventName}";
        return hookCommands;
    }

    public static string CreateConfigurationJson(IReadOnlyDictionary<string, string> hookCommandsByEvent)
        => CreateConfigurationJson(hookCommandsByEvent, HookCommandUtilities.GetCommandHookShellNameForCurrentPlatform());

    public static string CreateConfigurationJson(IReadOnlyDictionary<string, string> hookCommandsByEvent, string hookShellName)
    {
        var settingsObject = new JsonObject
        {
            [VersionPropertyName] = SupportedSchemaVersion,
            [HooksPropertyName] = CreateHooksObject(hookCommandsByEvent, hookShellName)
        };

        return settingsObject.ToJsonString(s_jsonSerializerOptions);
    }

    public static string CreateHooksJson(IReadOnlyDictionary<string, string> hookCommandsByEvent)
        => CreateHooksJson(hookCommandsByEvent, HookCommandUtilities.GetCommandHookShellNameForCurrentPlatform());

    public static string CreateHooksJson(IReadOnlyDictionary<string, string> hookCommandsByEvent, string hookShellName) => CreateHooksObject(hookCommandsByEvent, hookShellName).ToJsonString(s_jsonSerializerOptions);

    public static HookInstallationInspection InspectConfigurationJson(
        string configurationFilePath,
        string hookExecutablePath,
        string hookCommand,
        IReadOnlyDictionary<string, string> expectedHookCommands,
        string content,
        bool configurationFileExists)
        => InspectConfigurationJson(configurationFilePath, hookExecutablePath, hookCommand, expectedHookCommands, content, configurationFileExists, HookCommandUtilities.GetCommandHookShellNameForCurrentPlatform());

    public static HookInstallationInspection InspectConfigurationJson(
        string configurationFilePath,
        string hookExecutablePath,
        string hookCommand,
        IReadOnlyDictionary<string, string> expectedHookCommands,
        string content,
        bool configurationFileExists,
        string expectedHookShellName)
    {
        if (!TryParseConfigurationRoot(content, out var configurationRootObject, out var parseMessage))
        {
            return new HookInstallationInspection
            {
                ConfigurationFileExists = configurationFileExists,
                ConfigurationFilePath = configurationFilePath,
                HookCommand = hookCommand,
                HookExecutablePath = hookExecutablePath,
                Message = parseMessage,
                Provider = AgentProvider.GitHubCopilot,
                Status = HookInstallationStatus.Unknown
            };
        }

        var hasHooksProperty = configurationRootObject.TryGetPropertyValue(HooksPropertyName, out var hooksNode);
        if (!hasHooksProperty)
        {
            return new HookInstallationInspection
            {
                ConfigurationFileExists = configurationFileExists,
                ConfigurationFilePath = configurationFilePath,
                HookCommand = hookCommand,
                HookExecutablePath = hookExecutablePath,
                Message = "GitHub Copilot hook is not installed.",
                Provider = AgentProvider.GitHubCopilot,
                Status = HookInstallationStatus.NotInstalled
            };
        }

        if (hooksNode is not JsonObject hooksObject)
        {
            return new HookInstallationInspection
            {
                ConfigurationFileExists = configurationFileExists,
                ConfigurationFilePath = configurationFilePath,
                Checks = new Dictionary<HookInstallationCheck, bool>
                {
                    [HookInstallationCheck.HooksObject] = true
                },
                HookCommand = hookCommand,
                HookExecutablePath = hookExecutablePath,
                Message = "GitHub Copilot hooks setting must be a JSON object.",
                Provider = AgentProvider.GitHubCopilot,
                Status = HookInstallationStatus.Unknown
            };
        }

        var hasManagedHookEntries = false;
        var hasExpectedHookCommands = true;
        var hasExpectedNotificationMatcher = true;
        var hasSessionStartHook = false;
        var hasSessionEndHook = false;
        var hasUserPromptSubmittedHook = false;
        var hasPreToolUseHook = false;
        var hasPostToolUseHook = false;
        var hasPermissionRequestHook = false;
        var hasAgentStopHook = false;
        var hasSubagentStartHook = false;
        var hasSubagentStopHook = false;
        var hasErrorOccurredHook = false;
        var hasNotificationHook = false;

        foreach (var hookDefinition in s_requiredHookDefinitions)
        {
            if (!expectedHookCommands.TryGetValue(hookDefinition.HookEventName, out var expectedHookCommand))
            {
                return new HookInstallationInspection
                {
                    ConfigurationFileExists = configurationFileExists,
                    ConfigurationFilePath = configurationFilePath,
                    Checks = new Dictionary<HookInstallationCheck, bool>
                    {
                        [HookInstallationCheck.HooksObject] = true
                    },
                    HookCommand = hookCommand,
                    HookExecutablePath = hookExecutablePath,
                    Message = $"Missing expected hook command for '{hookDefinition.HookEventName}'.",
                    Provider = AgentProvider.GitHubCopilot,
                    Status = HookInstallationStatus.Unknown
                };
            }

            if (!TryInspectHookEvent(hooksObject, hookDefinition.HookEventName, expectedHookCommand, hookDefinition.Matcher, expectedHookShellName, out var hookEventInspection, out parseMessage))
            {
                return new HookInstallationInspection
                {
                    ConfigurationFileExists = configurationFileExists,
                    ConfigurationFilePath = configurationFilePath,
                    Checks = new Dictionary<HookInstallationCheck, bool>
                    {
                        [HookInstallationCheck.HooksObject] = true
                    },
                    HookCommand = hookCommand,
                    HookExecutablePath = hookExecutablePath,
                    Message = parseMessage,
                    Provider = AgentProvider.GitHubCopilot,
                    Status = HookInstallationStatus.Unknown
                };
            }

            hasManagedHookEntries |= hookEventInspection.HasManagedHook;
            hasExpectedHookCommands &= hookEventInspection.HasExpectedCommand;
            hasExpectedNotificationMatcher &= hookEventInspection.HasExpectedMatcher;

            switch (hookDefinition.HookEventName)
            {
                case GitHubCopilotHookEventNames.SessionStart:
                    hasSessionStartHook = hookEventInspection.HasManagedHook;
                    break;
                case GitHubCopilotHookEventNames.SessionEnd:
                    hasSessionEndHook = hookEventInspection.HasManagedHook;
                    break;
                case GitHubCopilotHookEventNames.UserPromptSubmitted:
                    hasUserPromptSubmittedHook = hookEventInspection.HasManagedHook;
                    break;
                case GitHubCopilotHookEventNames.PreToolUse:
                    hasPreToolUseHook = hookEventInspection.HasManagedHook;
                    break;
                case GitHubCopilotHookEventNames.PostToolUse:
                    hasPostToolUseHook = hookEventInspection.HasManagedHook;
                    break;
                case GitHubCopilotHookEventNames.PermissionRequest:
                    hasPermissionRequestHook = hookEventInspection.HasManagedHook;
                    break;
                case GitHubCopilotHookEventNames.AgentStop:
                    hasAgentStopHook = hookEventInspection.HasManagedHook;
                    break;
                case GitHubCopilotHookEventNames.SubagentStart:
                    hasSubagentStartHook = hookEventInspection.HasManagedHook;
                    break;
                case GitHubCopilotHookEventNames.SubagentStop:
                    hasSubagentStopHook = hookEventInspection.HasManagedHook;
                    break;
                case GitHubCopilotHookEventNames.ErrorOccurred:
                    hasErrorOccurredHook = hookEventInspection.HasManagedHook;
                    break;
                case GitHubCopilotHookEventNames.Notification:
                    hasNotificationHook = hookEventInspection.HasManagedHook;
                    break;
            }
        }

        var hasExpectedHookTimeout = TryHasExpectedHookTimeouts(hooksObject, out parseMessage);
        if (!string.IsNullOrWhiteSpace(parseMessage))
        {
            return new HookInstallationInspection
            {
                ConfigurationFileExists = configurationFileExists,
                ConfigurationFilePath = configurationFilePath,
                Checks = new Dictionary<HookInstallationCheck, bool>
                {
                    [HookInstallationCheck.HooksObject] = true
                },
                HookCommand = hookCommand,
                HookExecutablePath = hookExecutablePath,
                Message = parseMessage,
                Provider = AgentProvider.GitHubCopilot,
                Status = HookInstallationStatus.Unknown
            };
        }

        var isInstalled = hasSessionStartHook
            && hasSessionEndHook
            && hasUserPromptSubmittedHook
            && hasPreToolUseHook
            && hasPostToolUseHook
            && hasPermissionRequestHook
            && hasAgentStopHook
            && hasSubagentStartHook
            && hasSubagentStopHook
            && hasErrorOccurredHook
            && hasNotificationHook
            && hasExpectedHookCommands
            && hasExpectedNotificationMatcher
            && hasExpectedHookTimeout;
        var status = isInstalled ? HookInstallationStatus.Installed : hasManagedHookEntries ? HookInstallationStatus.NeedsUpdate : HookInstallationStatus.NotInstalled;
        var message = isInstalled
            ? "GitHub Copilot hook is installed."
            : hasManagedHookEntries
                ? "GitHub Copilot hook is installed but needs update."
                : "GitHub Copilot hook is not installed.";

        return new HookInstallationInspection
        {
            ConfigurationFileExists = configurationFileExists,
            ConfigurationFilePath = configurationFilePath,
            Checks = new Dictionary<HookInstallationCheck, bool>
            {
                [HookInstallationCheck.AgentStopHook] = hasAgentStopHook,
                [HookInstallationCheck.ErrorOccurredHook] = hasErrorOccurredHook,
                [HookInstallationCheck.ExpectedHookCommands] = hasExpectedHookCommands,
                [HookInstallationCheck.ExpectedNotificationMatcher] = hasExpectedNotificationMatcher,
                [HookInstallationCheck.HooksObject] = true,
                [HookInstallationCheck.ManagedHookEntries] = hasManagedHookEntries,
                [HookInstallationCheck.NotificationHook] = hasNotificationHook,
                [HookInstallationCheck.PermissionRequestHook] = hasPermissionRequestHook,
                [HookInstallationCheck.PostToolUseHook] = hasPostToolUseHook,
                [HookInstallationCheck.PreToolUseHook] = hasPreToolUseHook,
                [HookInstallationCheck.SessionEndHook] = hasSessionEndHook,
                [HookInstallationCheck.SessionStartHook] = hasSessionStartHook,
                [HookInstallationCheck.SubagentStartHook] = hasSubagentStartHook,
                [HookInstallationCheck.SubagentStopHook] = hasSubagentStopHook,
                [HookInstallationCheck.UserPromptSubmittedHook] = hasUserPromptSubmittedHook
            },
            HookCommand = hookCommand,
            HookExecutablePath = hookExecutablePath,
            Message = message,
            Provider = AgentProvider.GitHubCopilot,
            Status = status
        };
    }

    public static bool TryInstallManagedHooks(
        string content,
        IReadOnlyDictionary<string, string> hookCommandsByEvent,
        out string updatedContent,
        out string message)
        => TryInstallManagedHooks(content, hookCommandsByEvent, HookCommandUtilities.GetCommandHookShellNameForCurrentPlatform(), out updatedContent, out message);

    public static bool TryInstallManagedHooks(
        string content,
        IReadOnlyDictionary<string, string> hookCommandsByEvent,
        string hookShellName,
        out string updatedContent,
        out string message)
    {
        updatedContent = string.Empty;
        if (!TryParseConfigurationRoot(content, out var configurationRootObject, out message)) return false;
        if (!JsonHookConfigurationDocument.TryGetOrCreateHooksObject(configurationRootObject, "GitHub Copilot hooks setting must be a JSON object.", out var hooksObject, out message)) return false;

        configurationRootObject[VersionPropertyName] = SupportedSchemaVersion;

        foreach (var hookDefinition in s_requiredHookDefinitions)
        {
            if (!hookCommandsByEvent.TryGetValue(hookDefinition.HookEventName, out var hookCommand))
            {
                message = $"Missing hook command for '{hookDefinition.HookEventName}'.";
                return false;
            }

            if (!TryUpsertManagedHook(hooksObject, hookDefinition.HookEventName, hookCommand, hookShellName, hookDefinition.GetStatusMessage(), hookDefinition.Matcher, out message)) return false;
        }

        updatedContent = configurationRootObject.ToJsonString(s_jsonSerializerOptions) + Environment.NewLine;
        return true;
    }

    public static bool TryRemoveManagedHooks(string content, out string updatedContent, out bool changed, out string message)
    {
        updatedContent = content;
        changed = false;
        if (!TryParseConfigurationRoot(content, out var configurationRootObject, out message)) return false;
        if (!configurationRootObject.TryGetPropertyValue(HooksPropertyName, out var hooksNode) || hooksNode is null) return true;
        if (hooksNode is not JsonObject hooksObject)
        {
            message = "GitHub Copilot hooks setting must be a JSON object.";
            return false;
        }

        foreach (var hookDefinition in s_requiredHookDefinitions) changed |= RemoveManagedHook(hooksObject, hookDefinition.HookEventName);

        if (!changed) return true;
        if (hooksObject.Count == 0) configurationRootObject.Remove(HooksPropertyName);

        updatedContent = configurationRootObject.ToJsonString(s_jsonSerializerOptions) + Environment.NewLine;
        return true;
    }

    public static bool TryRefreshManagedHookStatusMessages(string content, out string updatedContent, out bool changed, out string message)
        => TryRefreshManagedHooks(
            content,
            new Dictionary<string, string>(StringComparer.Ordinal),
            string.Empty,
            refreshCommand: false,
            out updatedContent,
            out changed,
            out message);

    public static bool TryRefreshManagedHooks(
        string content,
        IReadOnlyDictionary<string, string> hookCommandsByEvent,
        string hookShellName,
        bool refreshCommand,
        out string updatedContent,
        out bool changed,
        out string message)
    {
        updatedContent = content;
        changed = false;
        if (!TryParseConfigurationRoot(content, out var configurationRootObject, out message)) return false;
        if (!configurationRootObject.TryGetPropertyValue(HooksPropertyName, out var hooksNode) || hooksNode is null) return true;
        if (hooksNode is not JsonObject hooksObject)
        {
            message = "GitHub Copilot hooks setting must be a JSON object.";
            return false;
        }

        foreach (var hookDefinition in s_requiredHookDefinitions)
        {
            if (!TryRefreshManagedHook(
                hooksObject,
                hookDefinition.HookEventName,
                hookCommandsByEvent,
                hookShellName,
                hookDefinition.GetStatusMessage(),
                hookDefinition.Matcher,
                refreshCommand,
                out var hookChanged,
                out message))
            {
                return false;
            }

            changed |= hookChanged;
        }

        if (!changed) return true;

        updatedContent = configurationRootObject.ToJsonString(s_jsonSerializerOptions) + Environment.NewLine;
        return true;
    }

    private static JsonObject CreateHooksObject(IReadOnlyDictionary<string, string> hookCommandsByEvent, string hookShellName)
    {
        var hooksObject = new JsonObject();
        foreach (var hookDefinition in s_requiredHookDefinitions)
        {
            if (!hookCommandsByEvent.TryGetValue(hookDefinition.HookEventName, out var hookCommand)) throw new InvalidOperationException($"Missing hook command for '{hookDefinition.HookEventName}'.");

            hooksObject[hookDefinition.HookEventName] = JsonHookConfigurationDocument.CreateJsonArrayWithSingleNode(CreateManagedHookDefinition(hookCommand, hookShellName, hookDefinition.GetStatusMessage(), hookDefinition.Matcher));
        }

        return hooksObject;
    }

    private static JsonObject CreateManagedHookDefinition(string hookCommand, string hookShellName, string statusMessage, string matcher)
    {
        var hookDefinitionObject = new JsonObject
        {
            ["type"] = CommandHookTypeName,
            [hookShellName] = hookCommand,
            ["timeoutSec"] = GetExpectedTimeoutSeconds(),
            ["statusMessage"] = statusMessage
        };

        if (!string.IsNullOrWhiteSpace(matcher)) hookDefinitionObject["matcher"] = matcher;
        return hookDefinitionObject;
    }

    private static string GetAliasEventName(string hookEventName) => GitHubCopilotHookEventNames.GetPascalCaseAlias(hookEventName);

    private static string GetCommandString(JsonObject hookDefinitionObject)
    {
        var currentPlatformShellName = HookCommandUtilities.GetCommandHookShellNameForCurrentPlatform();
        var currentPlatformCommand = JsonHookConfigurationDocument.GetStringProperty(hookDefinitionObject, currentPlatformShellName);
        if (!string.IsNullOrWhiteSpace(currentPlatformCommand)) return currentPlatformCommand;

        var alternatePlatformShellName = currentPlatformShellName.Equals("powershell", StringComparison.Ordinal)
            ? "bash"
            : "powershell";
        var alternatePlatformCommand = JsonHookConfigurationDocument.GetStringProperty(hookDefinitionObject, alternatePlatformShellName);
        if (!string.IsNullOrWhiteSpace(alternatePlatformCommand)) return alternatePlatformCommand;

        return JsonHookConfigurationDocument.GetStringProperty(hookDefinitionObject, "command");
    }

    private static bool IsLidGuardGitHubCopilotHookCommand(string command, string expectedHookEventName)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        if (!command.Contains("lidguard", StringComparison.OrdinalIgnoreCase)) return false;
        if (!command.Contains("copilot-hook", StringComparison.OrdinalIgnoreCase)) return false;
        if (command.Contains($"--event {expectedHookEventName}", StringComparison.OrdinalIgnoreCase)) return true;
        return expectedHookEventName.Equals(GitHubCopilotHookEventNames.AgentStop, StringComparison.Ordinal)
            && command.Contains($"--event {GitHubCopilotHookEventNames.PascalCaseAgentStopAlias}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasExpectedHookCommand(JsonObject hookDefinitionObject, string expectedHookCommand, string expectedHookShellName)
    {
        if (!JsonHookConfigurationDocument.GetStringProperty(hookDefinitionObject, "type").Equals(CommandHookTypeName, StringComparison.Ordinal)) return false;
        return JsonHookConfigurationDocument.GetStringProperty(hookDefinitionObject, expectedHookShellName).Equals(expectedHookCommand, StringComparison.Ordinal);
    }

    private static int GetExpectedTimeoutSeconds() => ManagedHookTimeoutConfiguration.GetInstalledHookTimeoutSeconds();

    private static bool RemoveManagedHook(JsonObject hooksObject, string hookEventName)
        => JsonHookConfigurationDocument.RemoveFlatManagedCommandHooks(
            hooksObject,
            hookEventName,
            GetSupportedEventNames(hookEventName),
            GetCommandString,
            IsLidGuardGitHubCopilotHookCommand);

    private static bool TryRefreshManagedHookStatusMessage(JsonObject hooksObject, string hookEventName, string statusMessage, out bool changed, out string message)
        => JsonHookConfigurationDocument.TryRefreshFlatManagedHookStatusMessage(
            hooksObject,
            hookEventName,
            GetSupportedEventNames(hookEventName),
            statusMessage,
            "GitHub Copilot",
            GetCommandString,
            IsLidGuardGitHubCopilotHookCommand,
            out changed,
            out message);

    private static bool TryHasExpectedHookTimeouts(JsonObject hooksObject, out string message)
    {
        message = string.Empty;
        var expectedTimeoutSeconds = GetExpectedTimeoutSeconds();
        foreach (var hookDefinition in s_requiredHookDefinitions) if (!TryHasExpectedHookTimeout(hooksObject, hookDefinition.HookEventName, expectedTimeoutSeconds, out message)) return false;

        return true;
    }

    private static bool TryHasExpectedHookTimeout(JsonObject hooksObject, string hookEventName, int expectedTimeoutSeconds, out string message)
    {
        message = string.Empty;
        foreach (var supportedHookEventName in GetSupportedEventNames(hookEventName))
        {
            if (!hooksObject.TryGetPropertyValue(supportedHookEventName, out var hookEventNode) || hookEventNode is null) continue;
            if (hookEventNode is not JsonArray hookDefinitions)
            {
                message = $"GitHub Copilot hook event '{supportedHookEventName}' must be a JSON array.";
                return false;
            }

            foreach (var hookDefinitionNode in hookDefinitions)
            {
                if (hookDefinitionNode is not JsonObject hookDefinitionObject)
                {
                    message = $"GitHub Copilot hook definition for '{supportedHookEventName}' must be a JSON object.";
                    return false;
                }

                if (!IsLidGuardGitHubCopilotHookCommand(GetCommandString(hookDefinitionObject), hookEventName)) continue;
                return HasSufficientTimeoutValue(hookDefinitionObject, expectedTimeoutSeconds);
            }
        }

        return false;
    }

    private static bool TryRefreshManagedHook(
        JsonObject hooksObject,
        string hookEventName,
        IReadOnlyDictionary<string, string> hookCommandsByEvent,
        string hookShellName,
        string statusMessage,
        string matcher,
        bool refreshCommand,
        out bool changed,
        out string message)
    {
        changed = false;
        message = string.Empty;
        foreach (var supportedHookEventName in GetSupportedEventNames(hookEventName))
        {
            if (!hooksObject.TryGetPropertyValue(supportedHookEventName, out var hookEventNode) || hookEventNode is null) continue;
            if (hookEventNode is not JsonArray hookDefinitions)
            {
                message = $"GitHub Copilot hook event '{supportedHookEventName}' must be a JSON array.";
                return false;
            }

            foreach (var hookDefinitionNode in hookDefinitions)
            {
                if (hookDefinitionNode is not JsonObject hookDefinitionObject)
                {
                    message = $"GitHub Copilot hook definition for '{supportedHookEventName}' must be a JSON object.";
                    return false;
                }

                if (!IsLidGuardGitHubCopilotHookCommand(GetCommandString(hookDefinitionObject), hookEventName)) continue;
                if (refreshCommand)
                {
                    if (!hookCommandsByEvent.TryGetValue(hookEventName, out var expectedHookCommand))
                    {
                        message = $"Missing hook command for '{hookEventName}'.";
                        return false;
                    }

                    var actualMatcher = JsonHookConfigurationDocument.GetStringProperty(hookDefinitionObject, "matcher");
                    var expectedMatcher = matcher ?? string.Empty;
                    if (!HasExpectedHookCommand(hookDefinitionObject, expectedHookCommand, hookShellName)
                        || !HasExpectedTimeoutValue(hookDefinitionObject, GetExpectedTimeoutSeconds())
                        || !JsonHookConfigurationDocument.GetStringProperty(hookDefinitionObject, "statusMessage").Equals(statusMessage, StringComparison.Ordinal)
                        || !actualMatcher.Equals(expectedMatcher, StringComparison.Ordinal))
                    {
                        ReplaceManagedHookDefinition(hookDefinitionObject, expectedHookCommand, hookShellName, statusMessage, matcher);
                        changed = true;
                    }

                    continue;
                }

                if (!HasExpectedTimeoutValue(hookDefinitionObject, GetExpectedTimeoutSeconds()))
                {
                    hookDefinitionObject["timeoutSec"] = GetExpectedTimeoutSeconds();
                    changed = true;
                }

                if (!JsonHookConfigurationDocument.GetStringProperty(hookDefinitionObject, "statusMessage").Equals(statusMessage, StringComparison.Ordinal))
                {
                    hookDefinitionObject["statusMessage"] = statusMessage;
                    changed = true;
                }
            }
        }

        return true;
    }

    private static bool HasExpectedTimeoutValue(JsonObject hookDefinitionObject, int expectedTimeoutSeconds)
        => hookDefinitionObject["timeoutSec"] is JsonValue timeoutValue
            && timeoutValue.TryGetValue<int>(out var timeoutSeconds)
            && timeoutSeconds == expectedTimeoutSeconds;

    private static bool HasSufficientTimeoutValue(JsonObject hookDefinitionObject, int expectedTimeoutSeconds)
        => hookDefinitionObject["timeoutSec"] is JsonValue timeoutValue
            && timeoutValue.TryGetValue<int>(out var timeoutSeconds)
            && timeoutSeconds >= expectedTimeoutSeconds;

    private static void ReplaceManagedHookDefinition(JsonObject hookDefinitionObject, string hookCommand, string hookShellName, string statusMessage, string matcher)
    {
        hookDefinitionObject.Clear();
        hookDefinitionObject["type"] = CommandHookTypeName;
        hookDefinitionObject[hookShellName] = hookCommand;
        hookDefinitionObject["timeoutSec"] = GetExpectedTimeoutSeconds();
        hookDefinitionObject["statusMessage"] = statusMessage;
        if (!string.IsNullOrWhiteSpace(matcher)) hookDefinitionObject["matcher"] = matcher;
    }

    private static bool TryInspectHookEvent(
        JsonObject hooksObject,
        string hookEventName,
        string expectedHookCommand,
        string expectedMatcher,
        string expectedHookShellName,
        out JsonHookEventInspection hookEventInspection,
        out string message)
        => JsonHookConfigurationDocument.TryInspectFlatCommandHookEvent(
            hooksObject,
            hookEventName,
            GetSupportedEventNames(hookEventName),
            expectedHookCommand,
            expectedMatcher,
            "GitHub Copilot",
            GetCommandString,
            IsLidGuardGitHubCopilotHookCommand,
            (hookDefinitionObject, expectedCommand) => HasExpectedHookCommand(hookDefinitionObject, expectedCommand, expectedHookShellName),
            out hookEventInspection,
            out message);

    private static bool TryParseConfigurationRoot(string content, out JsonObject configurationRootObject, out string message)
    {
        configurationRootObject = new JsonObject();
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(content)) return true;

        try
        {
            var rootNode = JsonNode.Parse(content, documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            if (rootNode is JsonObject existingConfigurationRootObject)
            {
                configurationRootObject = existingConfigurationRootObject;
                return true;
            }

            message = "GitHub Copilot hook configuration must contain a JSON object.";
            return false;
        }
        catch (JsonException exception)
        {
            message = $"GitHub Copilot hook configuration could not be parsed: {exception.Message}";
            return false;
        }
    }

    private static bool TryUpsertManagedHook(
        JsonObject hooksObject,
        string hookEventName,
        string hookCommand,
        string hookShellName,
        string statusMessage,
        string matcher,
        out string message)
    {
        message = string.Empty;
        foreach (var compatibleHookEventName in GetSupportedEventNames(hookEventName))
        {
            if (!hooksObject.TryGetPropertyValue(compatibleHookEventName, out var hookEventNode) || hookEventNode is null) continue;
            if (hookEventNode is not JsonArray hookDefinitions)
            {
                message = $"GitHub Copilot hook event '{compatibleHookEventName}' must be a JSON array.";
                return false;
            }

            foreach (var hookDefinitionNode in hookDefinitions)
            {
                if (hookDefinitionNode is not JsonObject hookDefinitionObject)
                {
                    message = $"GitHub Copilot hook definition for '{compatibleHookEventName}' must be a JSON object.";
                    return false;
                }

                if (!IsLidGuardGitHubCopilotHookCommand(GetCommandString(hookDefinitionObject), hookEventName)) continue;

                ReplaceManagedHookDefinition(hookDefinitionObject, hookCommand, hookShellName, statusMessage, matcher);
                return true;
            }

            JsonHookConfigurationDocument.AddJsonNode(hookDefinitions, CreateManagedHookDefinition(hookCommand, hookShellName, statusMessage, matcher));
            return true;
        }

        hooksObject[hookEventName] = JsonHookConfigurationDocument.CreateJsonArrayWithSingleNode(CreateManagedHookDefinition(hookCommand, hookShellName, statusMessage, matcher));
        return true;
    }

    private static IEnumerable<string> GetSupportedEventNames(string hookEventName)
    {
        yield return hookEventName;

        var aliasHookEventName = GetAliasEventName(hookEventName);
        if (!string.IsNullOrWhiteSpace(aliasHookEventName)) yield return aliasHookEventName;
    }
}
