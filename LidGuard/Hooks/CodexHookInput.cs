using System.Text.Json.Serialization;

namespace LidGuard.Hooks;

public sealed class CodexHookInput : IHookCommandInput
{
    [JsonPropertyName("session_id")]
    public string SessionIdentifier { get; init => field = value ?? string.Empty; } = string.Empty;

    [JsonPropertyName("transcript_path")]
    public string TranscriptPath { get; init => field = value ?? string.Empty; } = string.Empty;

    [JsonPropertyName("cwd")]
    public string WorkingDirectory { get; init => field = value ?? string.Empty; } = string.Empty;

    [JsonPropertyName("hook_event_name")]
    public string HookEventName { get; init => field = value ?? string.Empty; } = string.Empty;

    [JsonPropertyName("model")]
    public string Model { get; init => field = value ?? string.Empty; } = string.Empty;

    [JsonPropertyName("prompt")]
    public string Prompt { get; init => field = value ?? string.Empty; } = string.Empty;

    [JsonPropertyName("stop_hook_active")]
    public bool StopHookActive { get; init; }

    [JsonPropertyName("last_assistant_message")]
    public string LastAssistantMessage { get; init => field = value ?? string.Empty; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; init => field = value ?? string.Empty; } = string.Empty;
}