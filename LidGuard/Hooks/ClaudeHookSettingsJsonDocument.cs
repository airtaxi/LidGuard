using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using LidGuard.Localization;
using LidGuard.Sessions;

namespace LidGuard.Hooks;

public static class ClaudeHookSettingsJsonDocument
{
    public const string ClaudeCodeSettingsSchemaUrl = "https://json.schemastore.org/claude-code-settings.json";

    private const string HooksPropertyName = JsonHookConfigurationDocument.HooksPropertyName;
    private const string PowerShellShellName = "powershell";
    private static readonly JsonSerializerOptions s_jsonSerializerOptions = new() { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All), WriteIndented = true };
    private static readonly (string HookEventName, Func<string> GetStatusMessage, string Matcher)[] s_requiredHookDefinitions =
    [
        (ClaudeHookEventNames.UserPromptSubmit, () => LidGuardText.HookStatusMessageStartingTurnProtection, string.Empty),
        (ClaudeHookEventNames.PreToolUse, () => LidGuardText.HookStatusMessageRecordingClaudeToolActivity, string.Empty),
        (ClaudeHookEventNames.PostToolUse, () => LidGuardText.HookStatusMessageRecordingClaudeToolCompletionActivity, string.Empty),
        (ClaudeHookEventNames.PostToolUseFailure, () => LidGuardText.HookStatusMessageRecordingClaudeFailedToolActivity, string.Empty),
        (ClaudeHookEventNames.SubagentStart, () => LidGuardText.HookStatusMessageRecordingClaudeSubagentActivity, string.Empty),
        (ClaudeHookEventNames.SubagentStop, () => LidGuardText.HookStatusMessageRecordingClaudeSubagentCompletionActivity, string.Empty),
        (ClaudeHookEventNames.TaskCreated, () => LidGuardText.HookStatusMessageRecordingClaudeBackgroundTaskActivity, string.Empty),
        (ClaudeHookEventNames.TaskCompleted, () => LidGuardText.HookStatusMessageRecordingClaudeBackgroundTaskCompletion, string.Empty),
        (ClaudeHookEventNames.Stop, () => LidGuardText.HookStatusMessageStoppingSessionProtection, string.Empty),
        (ClaudeHookEventNames.StopFailure, () => LidGuardText.HookStatusMessageStoppingSessionProtection, string.Empty),
        (ClaudeHookEventNames.Elicitation, () => LidGuardText.HookStatusMessageCancelingClosedLidElicitationRequest, string.Empty),
        (ClaudeHookEventNames.PermissionRequest, () => LidGuardText.HookStatusMessageRespondingToClosedLidPermissionRequest, string.Empty),
        (ClaudeHookEventNames.Notification, () => LidGuardText.HookStatusMessageRecordingClaudeSoftLockTelemetry, ClaudeSoftLockSignalSource.NotificationMatcher),
        (ClaudeHookEventNames.SessionEnd, () => LidGuardText.HookStatusMessageStoppingSessionProtection, string.Empty)
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

    public static HookInstallationInspection InspectSettingsJson(
        string configurationFilePath,
        string hookExecutablePath,
        string hookCommand,
        string content,
        bool configurationFileExists)
    {
        if (!TryParseSettingsRoot(content, out var settingsObject, out var parseMessage))
        {
            return new HookInstallationInspection
            {
                Provider = AgentProvider.Claude,
                Status = HookInstallationStatus.Unknown,
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
            return new HookInstallationInspection
            {
                Provider = AgentProvider.Claude,
                Status = HookInstallationStatus.NotInstalled,
                ConfigurationFilePath = configurationFilePath,
                HookExecutablePath = hookExecutablePath,
                HookCommand = hookCommand,
                ConfigurationFileExists = configurationFileExists,
                Message = "Claude hook is not installed."
            };
        }

        if (hooksNode is not JsonObject hooksObject)
        {
            return new HookInstallationInspection
            {
                Provider = AgentProvider.Claude,
                Status = HookInstallationStatus.Unknown,
                ConfigurationFilePath = configurationFilePath,
                HookExecutablePath = hookExecutablePath,
                HookCommand = hookCommand,
                ConfigurationFileExists = configurationFileExists,
                Message = "Claude hooks setting must be a JSON object."
            };
        }

        if (!TryInspectHookEvent(hooksObject, ClaudeHookEventNames.UserPromptSubmit, hookCommand, string.Empty, out var userPromptSubmitInspection, out parseMessage)
            || !TryInspectHookEvent(hooksObject, ClaudeHookEventNames.PreToolUse, hookCommand, string.Empty, out var preToolUseInspection, out parseMessage)
            || !TryInspectHookEvent(hooksObject, ClaudeHookEventNames.PostToolUse, hookCommand, string.Empty, out var postToolUseInspection, out parseMessage)
            || !TryInspectHookEvent(hooksObject, ClaudeHookEventNames.PostToolUseFailure, hookCommand, string.Empty, out var postToolUseFailureInspection, out parseMessage)
            || !TryInspectHookEvent(hooksObject, ClaudeHookEventNames.SubagentStart, hookCommand, string.Empty, out var subagentStartInspection, out parseMessage)
            || !TryInspectHookEvent(hooksObject, ClaudeHookEventNames.SubagentStop, hookCommand, string.Empty, out var subagentStopInspection, out parseMessage)
            || !TryInspectHookEvent(hooksObject, ClaudeHookEventNames.TaskCreated, hookCommand, string.Empty, out var taskCreatedInspection, out parseMessage)
            || !TryInspectHookEvent(hooksObject, ClaudeHookEventNames.TaskCompleted, hookCommand, string.Empty, out var taskCompletedInspection, out parseMessage)
            || !TryInspectHookEvent(hooksObject, ClaudeHookEventNames.Stop, hookCommand, string.Empty, out var stopInspection, out parseMessage)
            || !TryInspectHookEvent(hooksObject, ClaudeHookEventNames.StopFailure, hookCommand, string.Empty, out var stopFailureInspection, out parseMessage)
            || !TryInspectHookEvent(hooksObject, ClaudeHookEventNames.Elicitation, hookCommand, string.Empty, out var elicitationInspection, out parseMessage)
            || !TryInspectHookEvent(hooksObject, ClaudeHookEventNames.PermissionRequest, hookCommand, string.Empty, out var permissionRequestInspection, out parseMessage)
            || !TryInspectHookEvent(hooksObject, ClaudeHookEventNames.Notification, hookCommand, ClaudeSoftLockSignalSource.NotificationMatcher, out var notificationInspection, out parseMessage)
            || !TryInspectHookEvent(hooksObject, ClaudeHookEventNames.SessionEnd, hookCommand, string.Empty, out var sessionEndInspection, out parseMessage))
        {
            return new HookInstallationInspection
            {
                Provider = AgentProvider.Claude,
                Status = HookInstallationStatus.Unknown,
                ConfigurationFilePath = configurationFilePath,
                HookExecutablePath = hookExecutablePath,
                HookCommand = hookCommand,
                ConfigurationFileExists = configurationFileExists,
                Checks = new Dictionary<HookInstallationCheck, bool>
                {
                    [HookInstallationCheck.HooksObject] = true
                },
                Message = parseMessage
            };
        }

        var hasManagedHookEntries = userPromptSubmitInspection.HasManagedHook
            || preToolUseInspection.HasManagedHook
            || postToolUseInspection.HasManagedHook
            || postToolUseFailureInspection.HasManagedHook
            || subagentStartInspection.HasManagedHook
            || subagentStopInspection.HasManagedHook
            || taskCreatedInspection.HasManagedHook
            || taskCompletedInspection.HasManagedHook
            || stopInspection.HasManagedHook
            || stopFailureInspection.HasManagedHook
            || elicitationInspection.HasManagedHook
            || permissionRequestInspection.HasManagedHook
            || notificationInspection.HasManagedHook
            || sessionEndInspection.HasManagedHook;
        var hasExpectedHookCommand = userPromptSubmitInspection.HasExpectedCommand
            && preToolUseInspection.HasExpectedCommand
            && postToolUseInspection.HasExpectedCommand
            && postToolUseFailureInspection.HasExpectedCommand
            && subagentStartInspection.HasExpectedCommand
            && subagentStopInspection.HasExpectedCommand
            && taskCreatedInspection.HasExpectedCommand
            && taskCompletedInspection.HasExpectedCommand
            && stopInspection.HasExpectedCommand
            && stopFailureInspection.HasExpectedCommand
            && elicitationInspection.HasExpectedCommand
            && permissionRequestInspection.HasExpectedCommand
            && notificationInspection.HasExpectedCommand
            && sessionEndInspection.HasExpectedCommand;
        var hasExpectedNotificationMatcher = userPromptSubmitInspection.HasExpectedMatcher
            && preToolUseInspection.HasExpectedMatcher
            && postToolUseInspection.HasExpectedMatcher
            && postToolUseFailureInspection.HasExpectedMatcher
            && subagentStartInspection.HasExpectedMatcher
            && subagentStopInspection.HasExpectedMatcher
            && taskCreatedInspection.HasExpectedMatcher
            && taskCompletedInspection.HasExpectedMatcher
            && stopInspection.HasExpectedMatcher
            && stopFailureInspection.HasExpectedMatcher
            && elicitationInspection.HasExpectedMatcher
            && permissionRequestInspection.HasExpectedMatcher
            && notificationInspection.HasExpectedMatcher
            && sessionEndInspection.HasExpectedMatcher;
        var hasExpectedHookShell = userPromptSubmitInspection.HasExpectedShell
            && preToolUseInspection.HasExpectedShell
            && postToolUseInspection.HasExpectedShell
            && postToolUseFailureInspection.HasExpectedShell
            && subagentStartInspection.HasExpectedShell
            && subagentStopInspection.HasExpectedShell
            && taskCreatedInspection.HasExpectedShell
            && taskCompletedInspection.HasExpectedShell
            && stopInspection.HasExpectedShell
            && stopFailureInspection.HasExpectedShell
            && elicitationInspection.HasExpectedShell
            && permissionRequestInspection.HasExpectedShell
            && notificationInspection.HasExpectedShell
            && sessionEndInspection.HasExpectedShell;
        var isInstalled = userPromptSubmitInspection.HasManagedHook
            && preToolUseInspection.HasManagedHook
            && postToolUseInspection.HasManagedHook
            && postToolUseFailureInspection.HasManagedHook
            && subagentStartInspection.HasManagedHook
            && subagentStopInspection.HasManagedHook
            && taskCreatedInspection.HasManagedHook
            && taskCompletedInspection.HasManagedHook
            && stopInspection.HasManagedHook
            && stopFailureInspection.HasManagedHook
            && elicitationInspection.HasManagedHook
            && permissionRequestInspection.HasManagedHook
            && notificationInspection.HasManagedHook
            && sessionEndInspection.HasManagedHook
            && hasExpectedHookCommand
            && hasExpectedNotificationMatcher
            && hasExpectedHookShell;
        var status = isInstalled ? HookInstallationStatus.Installed : hasManagedHookEntries ? HookInstallationStatus.NeedsUpdate : HookInstallationStatus.NotInstalled;
        var message = isInstalled ? "Claude hook is installed." : hasManagedHookEntries ? "Claude hook is installed but needs update." : "Claude hook is not installed.";

        return new HookInstallationInspection
        {
            Provider = AgentProvider.Claude,
            Status = status,
            ConfigurationFilePath = configurationFilePath,
            HookExecutablePath = hookExecutablePath,
            HookCommand = hookCommand,
            ConfigurationFileExists = configurationFileExists,
            Checks = new Dictionary<HookInstallationCheck, bool>
            {
                [HookInstallationCheck.HooksObject] = true,
                [HookInstallationCheck.ManagedHookEntries] = hasManagedHookEntries,
                [HookInstallationCheck.ExpectedHookCommand] = hasExpectedHookCommand,
                [HookInstallationCheck.ExpectedNotificationMatcher] = hasExpectedNotificationMatcher,
                [HookInstallationCheck.ExpectedHookShell] = hasExpectedHookShell,
                [HookInstallationCheck.NotificationHook] = notificationInspection.HasManagedHook,
                [HookInstallationCheck.PostToolUseFailureHook] = postToolUseFailureInspection.HasManagedHook,
                [HookInstallationCheck.PostToolUseHook] = postToolUseInspection.HasManagedHook,
                [HookInstallationCheck.PreToolUseHook] = preToolUseInspection.HasManagedHook,
                [HookInstallationCheck.UserPromptSubmitHook] = userPromptSubmitInspection.HasManagedHook,
                [HookInstallationCheck.StopHook] = stopInspection.HasManagedHook,
                [HookInstallationCheck.StopFailureHook] = stopFailureInspection.HasManagedHook,
                [HookInstallationCheck.SubagentStartHook] = subagentStartInspection.HasManagedHook,
                [HookInstallationCheck.SubagentStopHook] = subagentStopInspection.HasManagedHook,
                [HookInstallationCheck.TaskCreatedHook] = taskCreatedInspection.HasManagedHook,
                [HookInstallationCheck.TaskCompletedHook] = taskCompletedInspection.HasManagedHook,
                [HookInstallationCheck.ElicitationHook] = elicitationInspection.HasManagedHook,
                [HookInstallationCheck.PermissionRequestHook] = permissionRequestInspection.HasManagedHook,
                [HookInstallationCheck.SessionEndHook] = sessionEndInspection.HasManagedHook
            },
            Message = message
        };
    }

    public static bool TryInstallManagedHooks(string content, string hookCommand, out string updatedContent, out string message)
    {
        updatedContent = string.Empty;
        if (!TryParseSettingsRoot(content, out var settingsObject, out message)) return false;
        if (!JsonHookConfigurationDocument.TryGetOrCreateHooksObject(settingsObject, "Claude hooks setting must be a JSON object.", out var hooksObject, out message)) return false;

        if (!settingsObject.TryGetPropertyValue("$schema", out _) && settingsObject.Count == 1) settingsObject["$schema"] = ClaudeCodeSettingsSchemaUrl;

        foreach (var hookDefinition in s_requiredHookDefinitions)
        {
            if (!TryUpsertManagedHook(
                hooksObject,
                hookDefinition.HookEventName,
                hookCommand,
                hookDefinition.GetStatusMessage(),
                hookDefinition.Matcher,
                out message))
                return false;
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

    public static bool TryRefreshManagedHookStatusMessages(string content, out string updatedContent, out bool changed, out string message)
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
            if (!TryRefreshManagedHookStatusMessage(hooksObject, hookDefinition.HookEventName, hookDefinition.GetStatusMessage(), out var hookChanged, out message)) return false;
            changed |= hookChanged;
        }

        if (!changed) return true;

        updatedContent = settingsObject.ToJsonString(s_jsonSerializerOptions) + Environment.NewLine;
        return true;
    }

    private static JsonObject CreateHooksObject(string hookCommand)
    {
        var hooksObject = new JsonObject();
        foreach (var hookDefinition in s_requiredHookDefinitions)
        {
            hooksObject[hookDefinition.HookEventName] = JsonHookConfigurationDocument.CreateJsonArrayWithSingleNode(
                CreateManagedHookMatcher(hookCommand, hookDefinition.GetStatusMessage(), hookDefinition.Matcher));
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

    private static bool TryInspectHookEvent(
        JsonObject hooksObject,
        string hookEventName,
        string expectedHookCommand,
        string expectedMatcher,
        out JsonHookEventInspection inspection,
        out string message)
        => JsonHookConfigurationDocument.TryInspectNestedCommandHookEvent(
            hooksObject,
            hookEventName,
            expectedHookCommand,
            expectedMatcher,
            "Claude",
            IsLidGuardClaudeHookCommand,
            HasExpectedHookCommand,
            HasExpectedHookShell,
            out inspection,
            out message);

    private static bool TryUpsertManagedHook(
        JsonObject hooksObject,
        string hookEventName,
        string hookCommand,
        string statusMessage,
        string matcher,
        out string message)
    {
        message = string.Empty;

        if (!hooksObject.TryGetPropertyValue(hookEventName, out var hookEventNode) || hookEventNode is null)
        {
            hooksObject[hookEventName] = JsonHookConfigurationDocument.CreateJsonArrayWithSingleNode(CreateManagedHookMatcher(hookCommand, statusMessage, matcher));
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

                var command = JsonHookConfigurationDocument.GetStringProperty(hookDefinitionObject, "command");
                if (!IsLidGuardClaudeHookCommand(command)) continue;

                ReplaceManagedHookDefinition(hookMatcherObject, hookDefinitionObject, hookCommand, statusMessage, matcher);
                return true;
            }
        }

        JsonHookConfigurationDocument.AddJsonNode(hookMatchers, CreateManagedHookMatcher(hookCommand, statusMessage, matcher));
        return true;
    }

    private static bool RemoveManagedHook(JsonObject hooksObject, string hookEventName)
        => JsonHookConfigurationDocument.RemoveNestedManagedCommandHooks(hooksObject, hookEventName, IsLidGuardClaudeHookCommand);

    private static bool TryRefreshManagedHookStatusMessage(JsonObject hooksObject, string hookEventName, string statusMessage, out bool changed, out string message)
        => JsonHookConfigurationDocument.TryRefreshNestedManagedHookStatusMessage(
            hooksObject,
            hookEventName,
            statusMessage,
            "Claude",
            IsLidGuardClaudeHookCommand,
            out changed,
            out message);

    private static JsonObject CreateManagedHookMatcher(string hookCommand, string statusMessage, string matcher)
    {
        var hookMatcherObject = new JsonObject
        {
            ["hooks"] = JsonHookConfigurationDocument.CreateJsonArrayWithSingleNode(CreateManagedHookDefinition(hookCommand, statusMessage))
        };

        if (!string.IsNullOrWhiteSpace(matcher)) hookMatcherObject["matcher"] = matcher;
        return hookMatcherObject;
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

    private static void ReplaceManagedHookDefinition(
        JsonObject hookMatcherObject,
        JsonObject hookDefinitionObject,
        string hookCommand,
        string statusMessage,
        string matcher)
    {
        if (string.IsNullOrWhiteSpace(matcher)) hookMatcherObject.Remove("matcher");
        else hookMatcherObject["matcher"] = matcher;

        hookDefinitionObject.Clear();
        hookDefinitionObject["type"] = "command";
        hookDefinitionObject["command"] = hookCommand;
        hookDefinitionObject["shell"] = PowerShellShellName;
        hookDefinitionObject["timeout"] = 30;
        hookDefinitionObject["statusMessage"] = statusMessage;
    }

    private static bool HasExpectedHookCommand(JsonObject hookDefinitionObject, string expectedHookCommand)
        => JsonHookConfigurationDocument.GetStringProperty(hookDefinitionObject, "command").Equals(expectedHookCommand, StringComparison.Ordinal);

    private static bool HasExpectedHookShell(JsonObject hookDefinitionObject)
        => JsonHookConfigurationDocument.GetStringProperty(hookDefinitionObject, "shell").Equals(PowerShellShellName, StringComparison.OrdinalIgnoreCase);

    private static bool IsLidGuardClaudeHookCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        return command.Contains("lidguard", StringComparison.OrdinalIgnoreCase) && command.Contains("claude-hook", StringComparison.OrdinalIgnoreCase);
    }
}
