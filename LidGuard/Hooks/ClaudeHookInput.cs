using System.Text.Json;
using System.Text.Json.Serialization;

namespace LidGuard.Hooks;

public sealed class ClaudeHookInput : IHookCommandInput
{
    [JsonPropertyName("message")]
    public string NotificationMessage { get; init => field = value ?? string.Empty; } = string.Empty;

    [JsonPropertyName("notification_type")]
    public string NotificationType { get; init => field = value ?? string.Empty; } = string.Empty;

    [JsonPropertyName("session_id")]
    public string SessionIdentifier { get; init => field = value ?? string.Empty; } = string.Empty;

    [JsonPropertyName("title")]
    public string NotificationTitle { get; init => field = value ?? string.Empty; } = string.Empty;

    [JsonPropertyName("transcript_path")]
    public string TranscriptPath { get; init => field = value ?? string.Empty; } = string.Empty;

    [JsonPropertyName("cwd")]
    public string WorkingDirectory { get; init => field = value ?? string.Empty; } = string.Empty;

    [JsonPropertyName("hook_event_name")]
    public string HookEventName { get; init => field = value ?? string.Empty; } = string.Empty;

    [JsonPropertyName("is_interrupt")]
    public bool IsInterrupt { get; init; }

    [JsonPropertyName("permission_mode")]
    public string PermissionMode { get; init => field = value ?? string.Empty; } = string.Empty;

    [JsonPropertyName("prompt")]
    public string Prompt { get; init => field = value ?? string.Empty; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; init => field = value ?? string.Empty; } = string.Empty;

    [JsonPropertyName("stop_hook_active")]
    public bool StopHookActive { get; init; }

    [JsonPropertyName("agent_id")]
    public string AgentIdentifier { get; init => field = value ?? string.Empty; } = string.Empty;

    [JsonPropertyName("agent_type")]
    public string AgentType { get; init => field = value ?? string.Empty; } = string.Empty;

    [JsonPropertyName("agent_transcript_path")]
    public string AgentTranscriptPath { get; init => field = value ?? string.Empty; } = string.Empty;

    [JsonPropertyName("task_id")]
    public string TaskIdentifier { get; init => field = value ?? string.Empty; } = string.Empty;

    [JsonPropertyName("task_subject")]
    public string TaskSubject { get; init => field = value ?? string.Empty; } = string.Empty;

    [JsonPropertyName("task_description")]
    public string TaskDescription { get; init => field = value ?? string.Empty; } = string.Empty;

    [JsonPropertyName("teammate_name")]
    public string TeammateName { get; init => field = value ?? string.Empty; } = string.Empty;

    [JsonPropertyName("team_name")]
    public string TeamName { get; init => field = value ?? string.Empty; } = string.Empty;

    [JsonPropertyName("tool_name")]
    public string ToolName { get; init => field = value ?? string.Empty; } = string.Empty;

    [JsonPropertyName("tool_use_id")]
    public string ToolUseIdentifier { get; init => field = value ?? string.Empty; } = string.Empty;

    [JsonPropertyName("last_assistant_message")]
    public string LastAssistantMessage { get; init => field = value ?? string.Empty; } = string.Empty;

    [JsonPropertyName("tool_input")]
    public JsonElement ToolInput { get; init; }

    [JsonPropertyName("tool_response")]
    public JsonElement ToolResponse { get; init; }
}