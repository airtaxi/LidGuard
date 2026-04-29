using System.Text.Json.Serialization;

namespace LidGuardLib.Commons.Hooks;

public sealed class ClaudeHookInput
{
    [JsonPropertyName("message")]
    public string NotificationMessage { get; init; } = string.Empty;

    [JsonPropertyName("notification_type")]
    public string NotificationType { get; init; } = string.Empty;

    [JsonPropertyName("session_id")]
    public string SessionIdentifier { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string NotificationTitle { get; init; } = string.Empty;

    [JsonPropertyName("transcript_path")]
    public string TranscriptPath { get; init; } = string.Empty;

    [JsonPropertyName("cwd")]
    public string WorkingDirectory { get; init; } = string.Empty;

    [JsonPropertyName("hook_event_name")]
    public string HookEventName { get; init; } = string.Empty;

    [JsonPropertyName("permission_mode")]
    public string PermissionMode { get; init; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;

    [JsonPropertyName("tool_name")]
    public string ToolName { get; init; } = string.Empty;
}
