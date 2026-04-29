using System.Text.Json;
using System.Text.Json.Nodes;
using LidGuardLib.Commons.Sessions;

namespace LidGuardLib.Commons.Hooks;

public static class ClaudeHookSettingsJsonDocument
{
    public const string ClaudeCodeSettingsSchemaUrl = "https://json.schemastore.org/claude-code-settings.json";

    private const string HooksPropertyName = "hooks";
    private const string PowerShellShellName = "powershell";
    private const string StartStatusMessage = "Starting LidGuard turn protection";
    private const string PermissionRequestStatusMessage = "Responding to closed-lid permission request";
    private const string StopStatusMessage = "Stopping LidGuard session protection";
    private static readonly JsonSerializerOptions s_jsonSerializerOptions = new() { WriteIndented = true };
    private static readonly (string HookEventName, string StatusMessage)[] s_requiredHookDefinitions =
    [
        (ClaudeHookEventNames.UserPromptSubmit, StartStatusMessage),
        (ClaudeHookEventNames.Stop, StopStatusMessage),
        (ClaudeHookEventNames.StopFailure, StopStatusMessage),
        (ClaudeHookEventNames.PermissionRequest, PermissionRequestStatusMessage),
        (ClaudeHookEventNames.SessionEnd, StopStatusMessage)
    ];

    public static string CreateSettingsJsonSnippet(string hookCommand)
    {
        var settingsObject = new JsonObject
        {
            ["$schema"] = ClaudeCodeSettingsSchemaUrl,
            [HooksPropertyName] = CreateHooksObject(hookCommand)
        };

        return settingsObject.ToJsonString(s_jsonSerializerOptions);
    }

    public static string CreateHooksJsonSnippet(string hookCommand) => CreateHooksObject(hookCommand).ToJsonString(s_jsonSerializerOptions);

    public static ClaudeHookInstallationInspection InspectSettingsJson(
        string configurationFilePath,
        string hookExecutablePath,
        string hookCommand,
        string content,
        bool configurationFileExists)
    {
        if (!TryParseSettingsRoot(content, out var settingsObject, out var parseMessage))
        {
            return new ClaudeHookInstallationInspection
            {
                Provider = AgentProvider.Claude,
                Status = CodexHookInstallationStatus.Unknown,
                ConfigurationFilePath = configurationFilePath,
                HookExecutablePath = hookExecutablePath,
                HookCommand = hookCommand,
                ConfigurationFileExists = configurationFileExists,
                Message = parseMessage
            };
        }

        var hasHooksProperty = settingsObject.TryGetPropertyValue(HooksPropertyName, out var hooksNode);
        if (!hasHooksProperty)
        {
            return new ClaudeHookInstallationInspection
            {
                Provider = AgentProvider.Claude,
                Status = CodexHookInstallationStatus.NotInstalled,
                ConfigurationFilePath = configurationFilePath,
                HookExecutablePath = hookExecutablePath,
                HookCommand = hookCommand,
                ConfigurationFileExists = configurationFileExists,
                HasHooksObject = false,
                Message = "Claude hook is not installed."
            };
        }

        if (hooksNode is not JsonObject hooksObject)
        {
            return new ClaudeHookInstallationInspection
            {
                Provider = AgentProvider.Claude,
                Status = CodexHookInstallationStatus.Unknown,
                ConfigurationFilePath = configurationFilePath,
                HookExecutablePath = hookExecutablePath,
                HookCommand = hookCommand,
                ConfigurationFileExists = configurationFileExists,
                Message = "Claude hooks setting must be a JSON object."
            };
        }

        if (!TryInspectHookEvent(hooksObject, ClaudeHookEventNames.UserPromptSubmit, hookCommand, out var userPromptSubmitInspection, out parseMessage)
            || !TryInspectHookEvent(hooksObject, ClaudeHookEventNames.Stop, hookCommand, out var stopInspection, out parseMessage)
            || !TryInspectHookEvent(hooksObject, ClaudeHookEventNames.StopFailure, hookCommand, out var stopFailureInspection, out parseMessage)
            || !TryInspectHookEvent(hooksObject, ClaudeHookEventNames.PermissionRequest, hookCommand, out var permissionRequestInspection, out parseMessage)
            || !TryInspectHookEvent(hooksObject, ClaudeHookEventNames.SessionEnd, hookCommand, out var sessionEndInspection, out parseMessage))
        {
            return new ClaudeHookInstallationInspection
            {
                Provider = AgentProvider.Claude,
                Status = CodexHookInstallationStatus.Unknown,
                ConfigurationFilePath = configurationFilePath,
                HookExecutablePath = hookExecutablePath,
                HookCommand = hookCommand,
                ConfigurationFileExists = configurationFileExists,
                HasHooksObject = true,
                Message = parseMessage
            };
        }

        var hasManagedHookEntries = userPromptSubmitInspection.HasManagedHook
            || stopInspection.HasManagedHook
            || stopFailureInspection.HasManagedHook
            || permissionRequestInspection.HasManagedHook
            || sessionEndInspection.HasManagedHook;
        var hasExpectedHookCommand = userPromptSubmitInspection.HasExpectedCommand
            && stopInspection.HasExpectedCommand
            && stopFailureInspection.HasExpectedCommand
            && permissionRequestInspection.HasExpectedCommand
            && sessionEndInspection.HasExpectedCommand;
        var hasExpectedHookShell = userPromptSubmitInspection.HasExpectedShell
            && stopInspection.HasExpectedShell
            && stopFailureInspection.HasExpectedShell
            && permissionRequestInspection.HasExpectedShell
            && sessionEndInspection.HasExpectedShell;
        var isInstalled = userPromptSubmitInspection.HasManagedHook
            && stopInspection.HasManagedHook
            && stopFailureInspection.HasManagedHook
            && permissionRequestInspection.HasManagedHook
            && sessionEndInspection.HasManagedHook
            && hasExpectedHookCommand
            && hasExpectedHookShell;
        var status = isInstalled ? CodexHookInstallationStatus.Installed : hasManagedHookEntries ? CodexHookInstallationStatus.NeedsUpdate : CodexHookInstallationStatus.NotInstalled;
        var message = isInstalled ? "Claude hook is installed." : hasManagedHookEntries ? "Claude hook is installed but needs update." : "Claude hook is not installed.";

        return new ClaudeHookInstallationInspection
        {
            Provider = AgentProvider.Claude,
            Status = status,
            ConfigurationFilePath = configurationFilePath,
            HookExecutablePath = hookExecutablePath,
            HookCommand = hookCommand,
            ConfigurationFileExists = configurationFileExists,
            HasHooksObject = true,
            HasManagedHookEntries = hasManagedHookEntries,
            HasExpectedHookCommand = hasExpectedHookCommand,
            HasExpectedHookShell = hasExpectedHookShell,
            HasUserPromptSubmitHook = userPromptSubmitInspection.HasManagedHook,
            HasStopHook = stopInspection.HasManagedHook,
            HasStopFailureHook = stopFailureInspection.HasManagedHook,
            HasPermissionRequestHook = permissionRequestInspection.HasManagedHook,
            HasSessionEndHook = sessionEndInspection.HasManagedHook,
            Message = message
        };
    }

    public static bool TryInstallManagedHooks(string content, string hookCommand, out string updatedContent, out string message)
    {
        updatedContent = string.Empty;
        if (!TryParseSettingsRoot(content, out var settingsObject, out message)) return false;
        if (!TryGetOrCreateHooksObject(settingsObject, out var hooksObject, out message)) return false;

        if (!settingsObject.TryGetPropertyValue("$schema", out _) && settingsObject.Count == 1) settingsObject["$schema"] = ClaudeCodeSettingsSchemaUrl;

        foreach (var hookDefinition in s_requiredHookDefinitions)
        {
            if (!TryUpsertManagedHook(hooksObject, hookDefinition.HookEventName, hookCommand, hookDefinition.StatusMessage, out message)) return false;
        }

        updatedContent = settingsObject.ToJsonString(s_jsonSerializerOptions) + Environment.NewLine;
        return true;
    }

    public static bool TryRemoveManagedHooks(string content, out string updatedContent, out bool changed, out string message)
    {
        updatedContent = content;
        changed = false;
        if (!TryParseSettingsRoot(content, out var settingsObject, out message)) return false;
        if (!settingsObject.TryGetPropertyValue(HooksPropertyName, out var hooksNode) || hooksNode is null) return true;
        if (hooksNode is not JsonObject hooksObject)
        {
            message = "Claude hooks setting must be a JSON object.";
            return false;
        }

        foreach (var hookDefinition in s_requiredHookDefinitions)
        {
            changed |= RemoveManagedHook(hooksObject, hookDefinition.HookEventName);
        }

        if (!changed) return true;
        if (hooksObject.Count == 0) settingsObject.Remove(HooksPropertyName);

        updatedContent = settingsObject.ToJsonString(s_jsonSerializerOptions) + Environment.NewLine;
        return true;
    }

    private static JsonObject CreateHooksObject(string hookCommand)
    {
        var hooksObject = new JsonObject();
        foreach (var hookDefinition in s_requiredHookDefinitions)
        {
            hooksObject[hookDefinition.HookEventName] = CreateJsonArrayWithSingleNode(CreateManagedHookMatcher(hookCommand, hookDefinition.StatusMessage));
        }

        return hooksObject;
    }

    private static bool TryParseSettingsRoot(string content, out JsonObject settingsObject, out string message)
    {
        settingsObject = new JsonObject();
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(content)) return true;

        try
        {
            var rootNode = JsonNode.Parse(content);
            if (rootNode is JsonObject existingSettingsObject)
            {
                settingsObject = existingSettingsObject;
                return true;
            }

            message = "Claude settings file must contain a JSON object.";
            return false;
        }
        catch (JsonException exception)
        {
            message = $"Claude settings file could not be parsed: {exception.Message}";
            return false;
        }
    }

    private static bool TryGetOrCreateHooksObject(JsonObject settingsObject, out JsonObject hooksObject, out string message)
    {
        message = string.Empty;

        if (!settingsObject.TryGetPropertyValue(HooksPropertyName, out var hooksNode) || hooksNode is null)
        {
            hooksObject = new JsonObject();
            settingsObject[HooksPropertyName] = hooksObject;
            return true;
        }

        if (hooksNode is JsonObject existingHooksObject)
        {
            hooksObject = existingHooksObject;
            return true;
        }

        hooksObject = new JsonObject();
        message = "Claude hooks setting must be a JSON object.";
        return false;
    }

    private static bool TryInspectHookEvent(JsonObject hooksObject, string hookEventName, string expectedHookCommand, out ClaudeHookEventInspection inspection, out string message)
    {
        inspection = default;
        message = string.Empty;
        if (!hooksObject.TryGetPropertyValue(hookEventName, out var hookEventNode) || hookEventNode is null) return true;
        if (hookEventNode is not JsonArray hookMatchers)
        {
            message = $"Claude hook event '{hookEventName}' must be a JSON array.";
            return false;
        }

        var hasManagedHook = false;
        var hasExpectedCommand = false;
        var hasExpectedShell = false;
        foreach (var hookMatcherNode in hookMatchers)
        {
            if (hookMatcherNode is not JsonObject hookMatcherObject)
            {
                message = $"Claude hook matcher for '{hookEventName}' must be a JSON object.";
                return false;
            }

            if (hookMatcherObject["hooks"] is not JsonArray hookDefinitions)
            {
                message = $"Claude hook matcher for '{hookEventName}' must contain a hooks array.";
                return false;
            }

            foreach (var hookDefinitionNode in hookDefinitions)
            {
                if (hookDefinitionNode is not JsonObject hookDefinitionObject)
                {
                    message = $"Claude hook definition for '{hookEventName}' must be a JSON object.";
                    return false;
                }

                var command = GetStringProperty(hookDefinitionObject, "command");
                if (!IsLidGuardClaudeHookCommand(command)) continue;

                hasManagedHook = true;
                if (string.Equals(command, expectedHookCommand, StringComparison.Ordinal)) hasExpectedCommand = true;
                if (GetStringProperty(hookDefinitionObject, "shell").Equals(PowerShellShellName, StringComparison.OrdinalIgnoreCase)) hasExpectedShell = true;
            }
        }

        inspection = new ClaudeHookEventInspection(hasManagedHook, hasExpectedCommand, hasExpectedShell);
        return true;
    }

    private static bool TryUpsertManagedHook(JsonObject hooksObject, string hookEventName, string hookCommand, string statusMessage, out string message)
    {
        message = string.Empty;

        if (!hooksObject.TryGetPropertyValue(hookEventName, out var hookEventNode) || hookEventNode is null)
        {
            hooksObject[hookEventName] = CreateJsonArrayWithSingleNode(CreateManagedHookMatcher(hookCommand, statusMessage));
            return true;
        }

        if (hookEventNode is not JsonArray hookMatchers)
        {
            message = $"Claude hook event '{hookEventName}' must be a JSON array.";
            return false;
        }

        foreach (var hookMatcherNode in hookMatchers)
        {
            if (hookMatcherNode is not JsonObject hookMatcherObject)
            {
                message = $"Claude hook matcher for '{hookEventName}' must be a JSON object.";
                return false;
            }

            if (hookMatcherObject["hooks"] is not JsonArray hookDefinitions)
            {
                message = $"Claude hook matcher for '{hookEventName}' must contain a hooks array.";
                return false;
            }

            foreach (var hookDefinitionNode in hookDefinitions)
            {
                if (hookDefinitionNode is not JsonObject hookDefinitionObject)
                {
                    message = $"Claude hook definition for '{hookEventName}' must be a JSON object.";
                    return false;
                }

                var command = GetStringProperty(hookDefinitionObject, "command");
                if (!IsLidGuardClaudeHookCommand(command)) continue;

                ReplaceManagedHookDefinition(hookDefinitionObject, hookCommand, statusMessage);
                return true;
            }
        }

        AddJsonNode(hookMatchers, CreateManagedHookMatcher(hookCommand, statusMessage));
        return true;
    }

    private static bool RemoveManagedHook(JsonObject hooksObject, string hookEventName)
    {
        if (!hooksObject.TryGetPropertyValue(hookEventName, out var hookEventNode) || hookEventNode is null) return false;
        if (hookEventNode is not JsonArray hookMatchers) return false;

        var changed = false;
        for (var hookMatcherIndex = hookMatchers.Count - 1; hookMatcherIndex >= 0; hookMatcherIndex--)
        {
            if (hookMatchers[hookMatcherIndex] is not JsonObject hookMatcherObject) continue;
            if (hookMatcherObject["hooks"] is not JsonArray hookDefinitions) continue;

            for (var hookDefinitionIndex = hookDefinitions.Count - 1; hookDefinitionIndex >= 0; hookDefinitionIndex--)
            {
                if (hookDefinitions[hookDefinitionIndex] is not JsonObject hookDefinitionObject) continue;

                var command = GetStringProperty(hookDefinitionObject, "command");
                if (!IsLidGuardClaudeHookCommand(command)) continue;

                hookDefinitions.RemoveAt(hookDefinitionIndex);
                changed = true;
            }

            if (hookDefinitions.Count > 0) continue;

            hookMatchers.RemoveAt(hookMatcherIndex);
            changed = true;
        }

        if (hookMatchers.Count > 0) return changed;

        hooksObject.Remove(hookEventName);
        return true;
    }

    private static JsonObject CreateManagedHookMatcher(string hookCommand, string statusMessage)
    {
        return new JsonObject
        {
            ["hooks"] = CreateJsonArrayWithSingleNode(CreateManagedHookDefinition(hookCommand, statusMessage))
        };
    }

    private static JsonObject CreateManagedHookDefinition(string hookCommand, string statusMessage)
    {
        return new JsonObject
        {
            ["type"] = "command",
            ["command"] = hookCommand,
            ["shell"] = PowerShellShellName,
            ["timeout"] = 30,
            ["statusMessage"] = statusMessage
        };
    }

    private static void ReplaceManagedHookDefinition(JsonObject hookDefinitionObject, string hookCommand, string statusMessage)
    {
        hookDefinitionObject.Clear();
        hookDefinitionObject["type"] = "command";
        hookDefinitionObject["command"] = hookCommand;
        hookDefinitionObject["shell"] = PowerShellShellName;
        hookDefinitionObject["timeout"] = 30;
        hookDefinitionObject["statusMessage"] = statusMessage;
    }

    private static bool IsLidGuardClaudeHookCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        return command.Contains("lidguard", StringComparison.OrdinalIgnoreCase) && command.Contains("claude-hook", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetStringProperty(JsonObject jsonObject, string propertyName)
    {
        var valueNode = jsonObject[propertyName];
        return valueNode is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var value) ? value : string.Empty;
    }

    private static JsonArray CreateJsonArrayWithSingleNode(JsonNode jsonNode)
    {
        var jsonArray = new JsonArray();
        AddJsonNode(jsonArray, jsonNode);
        return jsonArray;
    }

    private static void AddJsonNode(JsonArray jsonArray, JsonNode jsonNode) => jsonArray.Add(jsonNode);

    private readonly record struct ClaudeHookEventInspection(bool HasManagedHook, bool HasExpectedCommand, bool HasExpectedShell);
}
