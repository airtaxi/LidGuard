using System.Text.Json;

namespace LidGuard.Hooks;

public sealed class OpenCodeHookInput : IHookCommandInput
{
    public string CallIdentifier { get; init; } = string.Empty;

    public string EventIdentifier { get; init; } = string.Empty;

    public string HookEventName { get; init; } = string.Empty;

    public string MessageIdentifier { get; init; } = string.Empty;

    public string Permission { get; init; } = string.Empty;

    public string Prompt { get; init; } = string.Empty;

    public string SessionIdentifier { get; init; } = string.Empty;

    public string SessionStatus { get; init; } = string.Empty;

    public string ToolName { get; init; } = string.Empty;

    public string TranscriptPath { get; init; } = string.Empty;

    public string WorkingDirectory { get; init; } = string.Empty;

    public static bool TryParse(string hookInputJson, string configuredHookEventName, out OpenCodeHookInput hookInput, out string message)
    {
        hookInput = new OpenCodeHookInput();
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(hookInputJson))
        {
            message = "OpenCode hook input is empty.";
            return false;
        }

        try
        {
            using var hookInputDocument = JsonDocument.Parse(hookInputJson);
            if (hookInputDocument.RootElement.ValueKind != JsonValueKind.Object)
            {
                message = "OpenCode hook input must be a JSON object.";
                return false;
            }

            var hookInputElement = hookInputDocument.RootElement;
            var eventElement = GetElement(hookInputElement, "event");
            var eventPropertiesElement = GetElement(eventElement, "properties");
            var eventInfoElement = GetElement(eventPropertiesElement, "info");
            var eventToolElement = GetElement(eventPropertiesElement, "tool");
            var statusElement = GetElement(eventPropertiesElement, "status");

            hookInput = new OpenCodeHookInput
            {
                CallIdentifier = Coalesce(GetString(hookInputElement, "callID", "callId"), GetString(eventToolElement, "callID", "callId")),
                EventIdentifier = Coalesce(GetString(hookInputElement, "eventID", "eventId"), GetString(eventElement, "id")),
                HookEventName = Coalesce(configuredHookEventName, GetString(hookInputElement, "eventName"), GetString(eventElement, "type")),
                MessageIdentifier = Coalesce(GetString(hookInputElement, "messageID", "messageId"), GetString(eventToolElement, "messageID", "messageId"), GetString(eventPropertiesElement, "messageID", "messageId")),
                Permission = Coalesce(GetString(hookInputElement, "permission"), GetString(eventPropertiesElement, "permission")),
                Prompt = Coalesce(GetString(hookInputElement, "prompt"), GetPromptText(hookInputElement), GetPromptText(eventPropertiesElement)),
                SessionIdentifier = Coalesce(GetString(hookInputElement, "sessionID", "sessionId"), GetString(eventPropertiesElement, "sessionID", "sessionId"), GetString(eventInfoElement, "id")),
                SessionStatus = Coalesce(GetString(hookInputElement, "sessionStatus", "status"), GetString(statusElement, "type")),
                ToolName = Coalesce(GetString(hookInputElement, "toolName", "tool"), GetString(eventPropertiesElement, "tool")),
                TranscriptPath = GetString(hookInputElement, "transcriptPath", "transcript_path"),
                WorkingDirectory = Coalesce(GetString(hookInputElement, "workingDirectory", "cwd"), GetString(hookInputElement, "directory"), GetString(eventElement, "directory"))
            };

            return true;
        }
        catch (JsonException exception)
        {
            message = exception.Message;
            return false;
        }
    }

    private static string Coalesce(params string[] values)
    {
        foreach (var value in values) if (!string.IsNullOrWhiteSpace(value)) return value;
        return string.Empty;
    }

    private static JsonElement GetElement(JsonElement hookInputElement, string primaryPropertyName, string secondaryPropertyName = "") => HookJsonPropertyReader.GetElementProperty(hookInputElement, primaryPropertyName, secondaryPropertyName);

    private static string GetPromptText(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return string.Empty;
        if (!element.TryGetProperty("prompt", out var promptElement)) return string.Empty;
        if (promptElement.ValueKind == JsonValueKind.String) return promptElement.GetString() ?? string.Empty;
        if (promptElement.ValueKind == JsonValueKind.Object) return GetString(promptElement, "text");
        return string.Empty;
    }

    private static string GetString(JsonElement hookInputElement, string primaryPropertyName, string secondaryPropertyName = "") => HookJsonPropertyReader.GetStringProperty(hookInputElement, primaryPropertyName, secondaryPropertyName);
}
