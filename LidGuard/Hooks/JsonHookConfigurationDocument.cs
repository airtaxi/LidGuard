using System.Text.Json.Nodes;

namespace LidGuard.Hooks;

internal static class JsonHookConfigurationDocument
{
    public const string HooksPropertyName = "hooks";

    private const string HookDefinitionsPropertyName = "hooks";
    private const string MatcherPropertyName = "matcher";
    private const string StatusMessagePropertyName = "statusMessage";

    public static bool TryGetOrCreateHooksObject(JsonObject rootObject, string hooksObjectTypeErrorMessage, out JsonObject hooksObject, out string message)
    {
        message = string.Empty;

        if (!rootObject.TryGetPropertyValue(HooksPropertyName, out var hooksNode) || hooksNode is null)
        {
            hooksObject = new JsonObject();
            rootObject[HooksPropertyName] = hooksObject;
            return true;
        }

        if (hooksNode is JsonObject existingHooksObject)
        {
            hooksObject = existingHooksObject;
            return true;
        }

        hooksObject = new JsonObject();
        message = hooksObjectTypeErrorMessage;
        return false;
    }

    public static bool TryInspectNestedCommandHookEvent(JsonObject hooksObject, string hookEventName, string expectedHookCommand, string expectedMatcher, string providerDisplayName, Func<string, bool> isManagedHookCommand, Func<JsonObject, string, bool> hasExpectedHookCommand, Func<JsonObject, bool> hasExpectedHookShell, out JsonHookEventInspection inspection, out string message)
    {
        inspection = default;
        message = string.Empty;
        if (!hooksObject.TryGetPropertyValue(hookEventName, out var hookEventNode) || hookEventNode is null) return true;
        if (hookEventNode is not JsonArray hookMatchers)
        {
            message = $"{providerDisplayName} hook event '{hookEventName}' must be a JSON array.";
            return false;
        }

        var hasManagedHook = false;
        var hasExpectedCommand = false;
        var hasExpectedMatcher = false;
        var hasExpectedShell = false;
        foreach (var hookMatcherNode in hookMatchers)
        {
            if (hookMatcherNode is not JsonObject hookMatcherObject)
            {
                message = $"{providerDisplayName} hook matcher for '{hookEventName}' must be a JSON object.";
                return false;
            }

            if (hookMatcherObject[HookDefinitionsPropertyName] is not JsonArray hookDefinitions)
            {
                message = $"{providerDisplayName} hook matcher for '{hookEventName}' must contain a hooks array.";
                return false;
            }

            hasExpectedMatcher |= HasExpectedMatcher(GetStringProperty(hookMatcherObject, MatcherPropertyName), expectedMatcher);

            foreach (var hookDefinitionNode in hookDefinitions)
            {
                if (hookDefinitionNode is not JsonObject hookDefinitionObject)
                {
                    message = $"{providerDisplayName} hook definition for '{hookEventName}' must be a JSON object.";
                    return false;
                }

                if (!isManagedHookCommand(GetStringProperty(hookDefinitionObject, "command"))) continue;

                hasManagedHook = true;
                hasExpectedCommand |= hasExpectedHookCommand(hookDefinitionObject, expectedHookCommand);
                hasExpectedShell |= hasExpectedHookShell(hookDefinitionObject);
            }
        }

        inspection = new JsonHookEventInspection(hasManagedHook, hasExpectedCommand, hasExpectedMatcher, hasExpectedShell);
        return true;
    }

    public static bool TryInspectFlatCommandHookEvent(JsonObject hooksObject, string hookEventName, IEnumerable<string> supportedHookEventNames, string expectedHookCommand, string expectedMatcher, string providerDisplayName, Func<JsonObject, string> getHookCommand, Func<string, string, bool> isManagedHookCommand, Func<JsonObject, string, bool> hasExpectedHookCommand, out JsonHookEventInspection inspection, out string message)
    {
        inspection = default;
        message = string.Empty;
        foreach (var supportedHookEventName in supportedHookEventNames)
        {
            if (!hooksObject.TryGetPropertyValue(supportedHookEventName, out var hookEventNode) || hookEventNode is null) continue;
            if (hookEventNode is not JsonArray hookDefinitions)
            {
                message = $"{providerDisplayName} hook event '{supportedHookEventName}' must be a JSON array.";
                return false;
            }

            var hasManagedHook = false;
            var hasExpectedCommand = false;
            var hasExpectedMatcher = false;
            foreach (var hookDefinitionNode in hookDefinitions)
            {
                if (hookDefinitionNode is not JsonObject hookDefinitionObject)
                {
                    message = $"{providerDisplayName} hook definition for '{supportedHookEventName}' must be a JSON object.";
                    return false;
                }

                if (!isManagedHookCommand(getHookCommand(hookDefinitionObject), hookEventName)) continue;

                hasManagedHook = true;
                hasExpectedCommand |= hasExpectedHookCommand(hookDefinitionObject, expectedHookCommand);
                hasExpectedMatcher |= HasExpectedMatcher(GetStringProperty(hookDefinitionObject, MatcherPropertyName), expectedMatcher);
            }

            inspection = new JsonHookEventInspection(hasManagedHook, hasExpectedCommand, hasExpectedMatcher, true);
            return true;
        }

        inspection = new JsonHookEventInspection(false, false, string.IsNullOrWhiteSpace(expectedMatcher), true);
        return true;
    }

    public static bool RemoveNestedManagedCommandHooks(JsonObject hooksObject, string hookEventName, Func<string, bool> isManagedHookCommand)
    {
        if (!hooksObject.TryGetPropertyValue(hookEventName, out var hookEventNode) || hookEventNode is null) return false;
        if (hookEventNode is not JsonArray hookMatchers) return false;

        var changed = false;
        for (var hookMatcherIndex = hookMatchers.Count - 1; hookMatcherIndex >= 0; hookMatcherIndex--)
        {
            if (hookMatchers[hookMatcherIndex] is not JsonObject hookMatcherObject) continue;
            if (hookMatcherObject[HookDefinitionsPropertyName] is not JsonArray hookDefinitions) continue;

            for (var hookDefinitionIndex = hookDefinitions.Count - 1; hookDefinitionIndex >= 0; hookDefinitionIndex--)
            {
                if (hookDefinitions[hookDefinitionIndex] is not JsonObject hookDefinitionObject) continue;
                if (!isManagedHookCommand(GetStringProperty(hookDefinitionObject, "command"))) continue;

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

    public static bool RemoveFlatManagedCommandHooks(JsonObject hooksObject, string hookEventName, IEnumerable<string> supportedHookEventNames, Func<JsonObject, string> getHookCommand, Func<string, string, bool> isManagedHookCommand)
    {
        var changed = false;
        foreach (var supportedHookEventName in supportedHookEventNames)
        {
            if (!hooksObject.TryGetPropertyValue(supportedHookEventName, out var hookEventNode) || hookEventNode is not JsonArray hookDefinitions) continue;

            for (var hookDefinitionIndex = hookDefinitions.Count - 1; hookDefinitionIndex >= 0; hookDefinitionIndex--)
            {
                if (hookDefinitions[hookDefinitionIndex] is not JsonObject hookDefinitionObject) continue;
                if (!isManagedHookCommand(getHookCommand(hookDefinitionObject), hookEventName)) continue;

                hookDefinitions.RemoveAt(hookDefinitionIndex);
                changed = true;
            }

            if (hookDefinitions.Count > 0) continue;

            hooksObject.Remove(supportedHookEventName);
            changed = true;
        }

        return changed;
    }

    public static bool TryRefreshNestedManagedHookStatusMessage(JsonObject hooksObject, string hookEventName, string statusMessage, string providerDisplayName, Func<string, bool> isManagedHookCommand, out bool changed, out string message)
    {
        changed = false;
        message = string.Empty;
        if (!hooksObject.TryGetPropertyValue(hookEventName, out var hookEventNode) || hookEventNode is null) return true;
        if (hookEventNode is not JsonArray hookMatchers)
        {
            message = $"{providerDisplayName} hook event '{hookEventName}' must be a JSON array.";
            return false;
        }

        foreach (var hookMatcherNode in hookMatchers)
        {
            if (hookMatcherNode is not JsonObject hookMatcherObject)
            {
                message = $"{providerDisplayName} hook matcher for '{hookEventName}' must be a JSON object.";
                return false;
            }

            if (hookMatcherObject[HookDefinitionsPropertyName] is not JsonArray hookDefinitions)
            {
                message = $"{providerDisplayName} hook matcher for '{hookEventName}' must contain a hooks array.";
                return false;
            }

            foreach (var hookDefinitionNode in hookDefinitions)
            {
                if (hookDefinitionNode is not JsonObject hookDefinitionObject)
                {
                    message = $"{providerDisplayName} hook definition for '{hookEventName}' must be a JSON object.";
                    return false;
                }

                if (!isManagedHookCommand(GetStringProperty(hookDefinitionObject, "command"))) continue;
                if (GetStringProperty(hookDefinitionObject, StatusMessagePropertyName).Equals(statusMessage, StringComparison.Ordinal)) continue;

                hookDefinitionObject[StatusMessagePropertyName] = statusMessage;
                changed = true;
            }
        }

        return true;
    }

    public static bool TryRefreshFlatManagedHookStatusMessage(JsonObject hooksObject, string hookEventName, IEnumerable<string> supportedHookEventNames, string statusMessage, string providerDisplayName, Func<JsonObject, string> getHookCommand, Func<string, string, bool> isManagedHookCommand, out bool changed, out string message)
    {
        changed = false;
        message = string.Empty;
        foreach (var supportedHookEventName in supportedHookEventNames)
        {
            if (!hooksObject.TryGetPropertyValue(supportedHookEventName, out var hookEventNode) || hookEventNode is null) continue;
            if (hookEventNode is not JsonArray hookDefinitions)
            {
                message = $"{providerDisplayName} hook event '{supportedHookEventName}' must be a JSON array.";
                return false;
            }

            foreach (var hookDefinitionNode in hookDefinitions)
            {
                if (hookDefinitionNode is not JsonObject hookDefinitionObject)
                {
                    message = $"{providerDisplayName} hook definition for '{supportedHookEventName}' must be a JSON object.";
                    return false;
                }

                if (!isManagedHookCommand(getHookCommand(hookDefinitionObject), hookEventName)) continue;
                if (GetStringProperty(hookDefinitionObject, StatusMessagePropertyName).Equals(statusMessage, StringComparison.Ordinal)) continue;

                hookDefinitionObject[StatusMessagePropertyName] = statusMessage;
                changed = true;
            }
        }

        return true;
    }

    public static JsonArray CreateJsonArrayWithSingleNode(JsonNode jsonNode)
    {
        var jsonArray = new JsonArray();
        AddJsonNode(jsonArray, jsonNode);
        return jsonArray;
    }

    public static void AddJsonNode(JsonArray jsonArray, JsonNode jsonNode) => jsonArray.Add(jsonNode);

    public static string GetStringProperty(JsonObject jsonObject, string propertyName) => HookJsonPropertyReader.GetStringProperty(jsonObject, propertyName);

    private static bool HasExpectedMatcher(string actualMatcher, string expectedMatcher)
    {
        if (string.IsNullOrWhiteSpace(expectedMatcher)) return string.IsNullOrWhiteSpace(actualMatcher);
        return actualMatcher.Equals(expectedMatcher, StringComparison.Ordinal);
    }
}

internal readonly record struct JsonHookEventInspection(bool HasManagedHook, bool HasExpectedCommand, bool HasExpectedMatcher, bool HasExpectedShell);
