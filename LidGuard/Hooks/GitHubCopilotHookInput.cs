using System.Text.Json;

namespace LidGuard.Hooks;

public sealed class GitHubCopilotHookInput : IHookCommandInput
{
    public string AgentDisplayName { get; init; } = string.Empty;

    public string AgentName { get; init; } = string.Empty;

    public string ErrorContext { get; init; } = string.Empty;

    public string HookEventName { get; init; } = string.Empty;

    public string NotificationMessage { get; init; } = string.Empty;

    public string NotificationTitle { get; init; } = string.Empty;

    public string NotificationType { get; init; } = string.Empty;

    public string Prompt { get; init; } = string.Empty;

    public bool? Recoverable { get; init; }

    public string SessionEndReason { get; init; } = string.Empty;

    public string SessionIdentifier { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public string StopReason { get; init; } = string.Empty;

    public JsonElement ToolInput { get; init; }

    public string ToolName { get; init; } = string.Empty;

    public JsonElement ToolResult { get; init; }

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
                AgentDisplayName = GetString(hookInputElement, "agentDisplayName", "agent_display_name"),
                AgentName = GetString(hookInputElement, "agentName", "agent_name"),
                ErrorContext = GetString(hookInputElement, "errorContext", "error_context"),
                HookEventName = GetString(hookInputElement, "hookEventName", "hook_event_name"),
                NotificationMessage = GetString(hookInputElement, "message"),
                NotificationTitle = GetString(hookInputElement, "title"),
                NotificationType = GetString(hookInputElement, "notificationType", "notification_type"),
                Prompt = GetString(hookInputElement, "prompt"),
                Recoverable = GetBoolean(hookInputElement, "recoverable"),
                SessionEndReason = GetString(hookInputElement, "reason"),
                SessionIdentifier = GetString(hookInputElement, "sessionId", "session_id"),
                Source = GetString(hookInputElement, "source"),
                StopReason = GetString(hookInputElement, "stopReason", "stop_reason"),
                ToolInput = GetElement(hookInputElement, "toolArgs", "tool_input"),
                ToolName = GetString(hookInputElement, "toolName", "tool_name"),
                ToolResult = GetElement(hookInputElement, "toolResult", "tool_result"),
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

    private static JsonElement GetElement(JsonElement hookInputElement, string primaryPropertyName, string secondaryPropertyName = "")
        => HookJsonPropertyReader.GetElementProperty(hookInputElement, primaryPropertyName, secondaryPropertyName);

    private static bool? GetBoolean(JsonElement hookInputElement, string propertyName)
        => HookJsonPropertyReader.GetNullableBooleanProperty(hookInputElement, propertyName);

    private static string GetString(JsonElement hookInputElement, string primaryPropertyName, string secondaryPropertyName = "")
        => HookJsonPropertyReader.GetStringProperty(hookInputElement, primaryPropertyName, secondaryPropertyName);
}
