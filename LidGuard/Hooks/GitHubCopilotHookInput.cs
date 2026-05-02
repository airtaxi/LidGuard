using System.Text.Json;

namespace LidGuard.Hooks;

public sealed class GitHubCopilotHookInput
{
    public string ErrorContext { get; init; } = string.Empty;

    public string NotificationMessage { get; init; } = string.Empty;

    public string NotificationTitle { get; init; } = string.Empty;

    public string NotificationType { get; init; } = string.Empty;

    public string Prompt { get; init; } = string.Empty;

    public bool? Recoverable { get; init; }

    public string SessionEndReason { get; init; } = string.Empty;

    public string SessionIdentifier { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public string StopReason { get; init; } = string.Empty;

    public string ToolName { get; init; } = string.Empty;

    public string TranscriptPath { get; init; } = string.Empty;

    public string WorkingDirectory { get; init; } = string.Empty;

    public static bool TryParse(string hookInputJson, out GitHubCopilotHookInput hookInput, out string message)
    {
        hookInput = new GitHubCopilotHookInput();
        message = string.Empty;

        if (string.IsNullOrWhiteSpace(hookInputJson))
        {
            message = "GitHub Copilot hook input is empty.";
            return false;
        }

        try
        {
            using var hookInputDocument = JsonDocument.Parse(hookInputJson);
            if (hookInputDocument.RootElement.ValueKind != JsonValueKind.Object)
            {
                message = "GitHub Copilot hook input must be a JSON object.";
                return false;
            }

            var hookInputElement = hookInputDocument.RootElement;
            hookInput = new GitHubCopilotHookInput
            {
                ErrorContext = GetString(hookInputElement, "errorContext", "error_context"),
                NotificationMessage = GetString(hookInputElement, "message"),
                NotificationTitle = GetString(hookInputElement, "title"),
                NotificationType = GetString(hookInputElement, "notificationType", "notification_type"),
                Prompt = GetString(hookInputElement, "prompt"),
                Recoverable = GetBoolean(hookInputElement, "recoverable"),
                SessionEndReason = GetString(hookInputElement, "reason"),
                SessionIdentifier = GetString(hookInputElement, "sessionId", "session_id"),
                Source = GetString(hookInputElement, "source"),
                StopReason = GetString(hookInputElement, "stopReason", "stop_reason"),
                ToolName = GetString(hookInputElement, "toolName", "tool_name"),
                TranscriptPath = GetString(hookInputElement, "transcriptPath", "transcript_path"),
                WorkingDirectory = GetString(hookInputElement, "cwd")
            };

            return true;
        }
        catch (JsonException exception)
        {
            message = exception.Message;
            return false;
        }
    }

    private static bool? GetBoolean(JsonElement hookInputElement, string propertyName)
    {
        if (!hookInputElement.TryGetProperty(propertyName, out var propertyValue)) return null;
        if (propertyValue.ValueKind != JsonValueKind.True && propertyValue.ValueKind != JsonValueKind.False) return null;
        return propertyValue.GetBoolean();
    }

    private static string GetString(JsonElement hookInputElement, string primaryPropertyName, string secondaryPropertyName = "")
    {
        if (TryGetString(hookInputElement, primaryPropertyName, out var propertyValue)) return propertyValue;
        if (!string.IsNullOrWhiteSpace(secondaryPropertyName) && TryGetString(hookInputElement, secondaryPropertyName, out propertyValue)) return propertyValue;
        return string.Empty;
    }

    private static bool TryGetString(JsonElement hookInputElement, string propertyName, out string propertyValue)
    {
        propertyValue = string.Empty;
        if (!hookInputElement.TryGetProperty(propertyName, out var propertyElement)) return false;
        if (propertyElement.ValueKind != JsonValueKind.String) return false;

        propertyValue = propertyElement.GetString() ?? string.Empty;
        return true;
    }
}
