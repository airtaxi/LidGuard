using System.Text.Json.Serialization;

namespace LidGuardLib.Commons.Hooks;

public sealed class CodexHookInput
{
    [JsonPropertyName("session_id")]
    public string SessionIdentifier { get; init; } = string.Empty;

    [JsonPropertyName("transcript_path")]
    public string TranscriptPath { get; init; } = string.Empty;

    [JsonPropertyName("cwd")]
    public string WorkingDirectory { get; init; } = string.Empty;

    [JsonPropertyName("hook_event_name")]
    public string HookEventName { get; init; } = string.Empty;

    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; init; } = string.Empty;
}
