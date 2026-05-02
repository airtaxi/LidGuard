using System.Text.Json;
using System.Text.Json.Nodes;
using LidGuard.Sessions;

namespace LidGuard.Hooks;

public static class GitHubCopilotHookConfigurationJsonDocument
{
    private const string CommandHookTypeName = "command";
    private const string HooksPropertyName = "hooks";
    private const string NotificationMatcher = "permission_prompt|elicitation_dialog";
    private const int SupportedSchemaVersion = 1;
    private const int TimeoutSeconds = 30;
    private const string VersionPropertyName = "version";
    private static readonly JsonSerializerOptions s_jsonSerializerOptions = new() { WriteIndented = true };
    private static readonly (string HookEventName, string StatusMessage, string Matcher)[] s_requiredHookDefinitions =
    [
        (GitHubCopilotHookEventNames.SessionStart, "Recording GitHub Copilot session start", string.Empty),
        (GitHubCopilotHookEventNames.SessionEnd, "Recording GitHub Copilot session end", string.Empty),
        (GitHubCopilotHookEventNames.UserPromptSubmitted, "Starting LidGuard turn protection", string.Empty),
        (GitHubCopilotHookEventNames.PreToolUse, "Blocking closed-lid ask_user prompt", string.Empty),
        (GitHubCopilotHookEventNames.PostToolUse, "Recording GitHub Copilot tool completion activity", string.Empty),
        (GitHubCopilotHookEventNames.PermissionRequest, "Responding to closed-lid permission request", string.Empty),
        (GitHubCopilotHookEventNames.AgentStop, "Stopping LidGuard turn protection", string.Empty),
        (GitHubCopilotHookEventNames.ErrorOccurred, "Recording GitHub Copilot error telemetry", string.Empty),
        (GitHubCopilotHookEventNames.Notification, "Recording GitHub Copilot prompt telemetry", NotificationMatcher)
    ];

    public static IReadOnlyDictionary<string, string> CreateManagedHookCommands(string hookCommand)
    {
        var hookCommands = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var hookDefinition in s_requiredHookDefinitions) hookCommands[hookDefinition.HookEventName] = $"{hookCommand} --event {hookDefinition.HookEventName}";
        return hookCommands;
    }

    public static string CreateConfigurationJson(IReadOnlyDictionary<string, string> hookCommandsByEvent)
    {
        var settingsObject = new JsonObject
        {
            [VersionPropertyName] = SupportedSchemaVersion,
            [HooksPropertyName] = CreateHooksObject(hookCommandsByEvent)
        };

        return settingsObject.ToJsonString(s_jsonSerializerOptions);
    }

    public static string CreateHooksJson(IReadOnlyDictionary<string, string> hookCommandsByEvent) => CreateHooksObject(hookCommandsByEvent).ToJsonString(s_jsonSerializerOptions);

    public static GitHubCopilotHookInstallationInspection InspectConfigurationJson(
        string configurationFilePath,
        string hookExecutablePath,
        string hookCommand,
        IReadOnlyDictionary<string, string> expectedHookCommands,
        string content,
        bool configurationFileExists)
    {
        if (!TryParseConfigurationRoot(content, out var configurationRootObject, out var parseMessage))
        {
            return new GitHubCopilotHookInstallationInspection
            {
                ConfigurationFileExists = configurationFileExists,
                ConfigurationFilePath = configurationFilePath,
                HookCommand = hookCommand,
                HookExecutablePath = hookExecutablePath,
                Message = parseMessage,
                Provider = AgentProvider.GitHubCopilot,
                Status = CodexHookInstallationStatus.Unknown
            };
        }

        var hasHooksProperty = configurationRootObject.TryGetPropertyValue(HooksPropertyName, out var hooksNode);
        if (!hasHooksProperty)
        {
            return new GitHubCopilotHookInstallationInspection
            {
                ConfigurationFileExists = configurationFileExists,
                ConfigurationFilePath = configurationFilePath,
                HasHooksObject = false,
                HookCommand = hookCommand,
                HookExecutablePath = hookExecutablePath,
                Message = "GitHub Copilot hook is not installed.",
                Provider = AgentProvider.GitHubCopilot,
                Status = CodexHookInstallationStatus.NotInstalled
            };
        }

        if (hooksNode is not JsonObject hooksObject)
        {
            return new GitHubCopilotHookInstallationInspection
            {
                ConfigurationFileExists = configurationFileExists,
                ConfigurationFilePath = configurationFilePath,
                HasHooksObject = true,
                HookCommand = hookCommand,
                HookExecutablePath = hookExecutablePath,
                Message = "GitHub Copilot hooks setting must be a JSON object.",
                Provider = AgentProvider.GitHubCopilot,
                Status = CodexHookInstallationStatus.Unknown
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
        var hasErrorOccurredHook = false;
        var hasNotificationHook = false;

        foreach (var hookDefinition in s_requiredHookDefinitions)
        {
            if (!expectedHookCommands.TryGetValue(hookDefinition.HookEventName, out var expectedHookCommand))
            {
                return new GitHubCopilotHookInstallationInspection
                {
                    ConfigurationFileExists = configurationFileExists,
                    ConfigurationFilePath = configurationFilePath,
                    HasHooksObject = true,
                    HookCommand = hookCommand,
                    HookExecutablePath = hookExecutablePath,
                    Message = $"Missing expected hook command for '{hookDefinition.HookEventName}'.",
                    Provider = AgentProvider.GitHubCopilot,
                    Status = CodexHookInstallationStatus.Unknown
                };
            }

            if (!TryInspectHookEvent(hooksObject, hookDefinition.HookEventName, expectedHookCommand, hookDefinition.Matcher, out var hookEventInspection, out parseMessage))
            {
                return new GitHubCopilotHookInstallationInspection
                {
                    ConfigurationFileExists = configurationFileExists,
                    ConfigurationFilePath = configurationFilePath,
                    HasHooksObject = true,
                    HookCommand = hookCommand,
                    HookExecutablePath = hookExecutablePath,
                    Message = parseMessage,
                    Provider = AgentProvider.GitHubCopilot,
                    Status = CodexHookInstallationStatus.Unknown
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
                case GitHubCopilotHookEventNames.ErrorOccurred:
                    hasErrorOccurredHook = hookEventInspection.HasManagedHook;
                    break;
                case GitHubCopilotHookEventNames.Notification:
                    hasNotificationHook = hookEventInspection.HasManagedHook;
                    break;
            }
        }

        var isInstalled = hasSessionStartHook
            && hasSessionEndHook
            && hasUserPromptSubmittedHook
            && hasPreToolUseHook
            && hasPostToolUseHook
            && hasPermissionRequestHook
            && hasAgentStopHook
            && hasErrorOccurredHook
            && hasNotificationHook
            && hasExpectedHookCommands
            && hasExpectedNotificationMatcher;
        var status = isInstalled ? CodexHookInstallationStatus.Installed : hasManagedHookEntries ? CodexHookInstallationStatus.NeedsUpdate : CodexHookInstallationStatus.NotInstalled;
        var message = isInstalled
            ? "GitHub Copilot hook is installed."
            : hasManagedHookEntries
                ? "GitHub Copilot hook is installed but needs update."
                : "GitHub Copilot hook is not installed.";

        return new GitHubCopilotHookInstallationInspection
        {
            ConfigurationFileExists = configurationFileExists,
            ConfigurationFilePath = configurationFilePath,
            HasAgentStopHook = hasAgentStopHook,
            HasErrorOccurredHook = hasErrorOccurredHook,
            HasExpectedHookCommands = hasExpectedHookCommands,
            HasExpectedNotificationMatcher = hasExpectedNotificationMatcher,
            HasHooksObject = true,
            HasManagedHookEntries = hasManagedHookEntries,
            HasNotificationHook = hasNotificationHook,
            HasPermissionRequestHook = hasPermissionRequestHook,
            HasPostToolUseHook = hasPostToolUseHook,
            HasPreToolUseHook = hasPreToolUseHook,
            HasSessionEndHook = hasSessionEndHook,
            HasSessionStartHook = hasSessionStartHook,
            HasUserPromptSubmittedHook = hasUserPromptSubmittedHook,
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
    {
        updatedContent = string.Empty;
        if (!TryParseConfigurationRoot(content, out var configurationRootObject, out message)) return false;
        if (!TryGetOrCreateHooksObject(configurationRootObject, out var hooksObject, out message)) return false;

        configurationRootObject[VersionPropertyName] = SupportedSchemaVersion;

        foreach (var hookDefinition in s_requiredHookDefinitions)
        {
            if (!hookCommandsByEvent.TryGetValue(hookDefinition.HookEventName, out var hookCommand))
            {
                message = $"Missing hook command for '{hookDefinition.HookEventName}'.";
                return false;
            }

            if (!TryUpsertManagedHook(hooksObject, hookDefinition.HookEventName, hookCommand, hookDefinition.StatusMessage, hookDefinition.Matcher, out message)) return false;
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

        foreach (var hookDefinition in s_requiredHookDefinitions)
        {
            changed |= RemoveManagedHook(hooksObject, hookDefinition.HookEventName);
        }

        if (!changed) return true;
        if (hooksObject.Count == 0) configurationRootObject.Remove(HooksPropertyName);

        updatedContent = configurationRootObject.ToJsonString(s_jsonSerializerOptions) + Environment.NewLine;
        return true;
    }

    private static JsonObject CreateHooksObject(IReadOnlyDictionary<string, string> hookCommandsByEvent)
    {
        var hooksObject = new JsonObject();
        foreach (var hookDefinition in s_requiredHookDefinitions)
        {
            if (!hookCommandsByEvent.TryGetValue(hookDefinition.HookEventName, out var hookCommand))
            {
                throw new InvalidOperationException($"Missing hook command for '{hookDefinition.HookEventName}'.");
            }

            hooksObject[hookDefinition.HookEventName] = CreateJsonArrayWithSingleNode(CreateManagedHookDefinition(hookCommand, hookDefinition.StatusMessage, hookDefinition.Matcher));
        }

        return hooksObject;
    }

    private static JsonObject CreateManagedHookDefinition(string hookCommand, string statusMessage, string matcher)
    {
        var hookDefinitionObject = new JsonObject
        {
            ["type"] = CommandHookTypeName,
            ["powershell"] = hookCommand,
            ["timeoutSec"] = TimeoutSeconds,
            ["statusMessage"] = statusMessage
        };

        if (!string.IsNullOrWhiteSpace(matcher)) hookDefinitionObject["matcher"] = matcher;
        return hookDefinitionObject;
    }

    private static JsonArray CreateJsonArrayWithSingleNode(JsonNode jsonNode)
    {
        var jsonArray = new JsonArray();
        AddJsonNode(jsonArray, jsonNode);
        return jsonArray;
    }

    private static void AddJsonNode(JsonArray jsonArray, JsonNode jsonNode) => jsonArray.Add(jsonNode);

    private static string GetAliasEventName(string hookEventName) => GitHubCopilotHookEventNames.GetPascalCaseAlias(hookEventName);

    private static string GetCommandString(JsonObject hookDefinitionObject)
    {
        var powershellCommand = GetStringProperty(hookDefinitionObject, "powershell");
        if (!string.IsNullOrWhiteSpace(powershellCommand)) return powershellCommand;
        return GetStringProperty(hookDefinitionObject, "bash");
    }

    private static string GetStringProperty(JsonObject jsonObject, string propertyName)
    {
        var valueNode = jsonObject[propertyName];
        return valueNode is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var value) ? value : string.Empty;
    }

    private static bool HasExpectedMatcher(string actualMatcher, string expectedMatcher)
    {
        if (string.IsNullOrWhiteSpace(expectedMatcher)) return string.IsNullOrWhiteSpace(actualMatcher);
        return actualMatcher.Equals(expectedMatcher, StringComparison.Ordinal);
    }

    private static bool IsLidGuardGitHubCopilotHookCommand(string command, string expectedHookEventName)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        if (!command.Contains("lidguard", StringComparison.OrdinalIgnoreCase)) return false;
        if (!command.Contains("copilot-hook", StringComparison.OrdinalIgnoreCase)) return false;
        return command.Contains($"--event {expectedHookEventName}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool RemoveManagedHook(JsonObject hooksObject, string hookEventName)
    {
        var changed = false;
        foreach (var compatibleHookEventName in GetSupportedEventNames(hookEventName))
        {
            if (!hooksObject.TryGetPropertyValue(compatibleHookEventName, out var hookEventNode) || hookEventNode is not JsonArray hookDefinitions) continue;

            for (var hookDefinitionIndex = hookDefinitions.Count - 1; hookDefinitionIndex >= 0; hookDefinitionIndex--)
            {
                if (hookDefinitions[hookDefinitionIndex] is not JsonObject hookDefinitionObject) continue;
                if (!IsLidGuardGitHubCopilotHookCommand(GetCommandString(hookDefinitionObject), hookEventName)) continue;

                hookDefinitions.RemoveAt(hookDefinitionIndex);
                changed = true;
            }

            if (hookDefinitions.Count > 0) continue;

            hooksObject.Remove(compatibleHookEventName);
            changed = true;
        }

        return changed;
    }

    private static void ReplaceManagedHookDefinition(JsonObject hookDefinitionObject, string hookCommand, string statusMessage, string matcher)
    {
        hookDefinitionObject.Clear();
        hookDefinitionObject["type"] = CommandHookTypeName;
        hookDefinitionObject["powershell"] = hookCommand;
        hookDefinitionObject["timeoutSec"] = TimeoutSeconds;
        hookDefinitionObject["statusMessage"] = statusMessage;
        if (!string.IsNullOrWhiteSpace(matcher)) hookDefinitionObject["matcher"] = matcher;
    }

    private static bool TryGetOrCreateHooksObject(JsonObject configurationRootObject, out JsonObject hooksObject, out string message)
    {
        message = string.Empty;
        if (!configurationRootObject.TryGetPropertyValue(HooksPropertyName, out var hooksNode) || hooksNode is null)
        {
            hooksObject = new JsonObject();
            configurationRootObject[HooksPropertyName] = hooksObject;
            return true;
        }

        if (hooksNode is JsonObject existingHooksObject)
        {
            hooksObject = existingHooksObject;
            return true;
        }

        hooksObject = new JsonObject();
        message = "GitHub Copilot hooks setting must be a JSON object.";
        return false;
    }

    private static bool TryInspectHookEvent(
        JsonObject hooksObject,
        string hookEventName,
        string expectedHookCommand,
        string expectedMatcher,
        out GitHubCopilotHookEventInspection hookEventInspection,
        out string message)
    {
        hookEventInspection = default;
        message = string.Empty;
        foreach (var compatibleHookEventName in GetSupportedEventNames(hookEventName))
        {
            if (!hooksObject.TryGetPropertyValue(compatibleHookEventName, out var hookEventNode) || hookEventNode is null) continue;
            if (hookEventNode is not JsonArray hookDefinitions)
            {
                message = $"GitHub Copilot hook event '{compatibleHookEventName}' must be a JSON array.";
                return false;
            }

            var hasManagedHook = false;
            var hasExpectedCommand = false;
            var hasExpectedMatcher = false;
            foreach (var hookDefinitionNode in hookDefinitions)
            {
                if (hookDefinitionNode is not JsonObject hookDefinitionObject)
                {
                    message = $"GitHub Copilot hook definition for '{compatibleHookEventName}' must be a JSON object.";
                    return false;
                }

                var command = GetCommandString(hookDefinitionObject);
                if (!IsLidGuardGitHubCopilotHookCommand(command, hookEventName)) continue;

                hasManagedHook = true;
                hasExpectedCommand |= GetStringProperty(hookDefinitionObject, "type").Equals(CommandHookTypeName, StringComparison.Ordinal)
                    && GetStringProperty(hookDefinitionObject, "powershell").Equals(expectedHookCommand, StringComparison.Ordinal);
                hasExpectedMatcher |= HasExpectedMatcher(GetStringProperty(hookDefinitionObject, "matcher"), expectedMatcher);
            }

            hookEventInspection = new GitHubCopilotHookEventInspection(hasManagedHook, hasExpectedCommand, hasExpectedMatcher);
            return true;
        }

        hookEventInspection = new GitHubCopilotHookEventInspection(false, false, string.IsNullOrWhiteSpace(expectedMatcher));
        return true;
    }

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

                ReplaceManagedHookDefinition(hookDefinitionObject, hookCommand, statusMessage, matcher);
                if (!compatibleHookEventName.Equals(hookEventName, StringComparison.Ordinal))
                {
                    hooksObject.Remove(compatibleHookEventName);
                    hooksObject[hookEventName] = hookDefinitions;
                }

                return true;
            }

            AddJsonNode(hookDefinitions, CreateManagedHookDefinition(hookCommand, statusMessage, matcher));
            if (!compatibleHookEventName.Equals(hookEventName, StringComparison.Ordinal))
            {
                hooksObject.Remove(compatibleHookEventName);
                hooksObject[hookEventName] = hookDefinitions;
            }

            return true;
        }

        hooksObject[hookEventName] = CreateJsonArrayWithSingleNode(CreateManagedHookDefinition(hookCommand, statusMessage, matcher));
        return true;
    }

    private static IEnumerable<string> GetSupportedEventNames(string hookEventName)
    {
        yield return hookEventName;

        var aliasHookEventName = GetAliasEventName(hookEventName);
        if (!string.IsNullOrWhiteSpace(aliasHookEventName)) yield return aliasHookEventName;
    }

    private readonly record struct GitHubCopilotHookEventInspection(bool HasManagedHook, bool HasExpectedCommand, bool HasExpectedMatcher);
}
