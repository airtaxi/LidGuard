using System.Text.Json.Serialization;

namespace LidGuard.Runtime;

internal sealed class LidGuardWebhookRequest
{
    [JsonPropertyName("eventType")]
    public string EventType { get; init; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;

    [JsonPropertyName("softLockedSessionCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SoftLockedSessionCount { get; init; }

    [JsonPropertyName("provider")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Provider { get; init; }

    [JsonPropertyName("providerName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string ProviderName { get; init; }

    [JsonPropertyName("sessionIdentifier")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string SessionIdentifier { get; init; }

    [JsonPropertyName("startedAtUtc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? StartedAtUtc { get; init; }

    [JsonPropertyName("lastActivityAtUtc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? LastActivityAtUtc { get; init; }

    [JsonPropertyName("endedAtUtc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? EndedAtUtc { get; init; }

    [JsonPropertyName("endReason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string EndReason { get; init; }

    [JsonPropertyName("activeSessionCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ActiveSessionCount { get; init; }

    [JsonPropertyName("workingDirectory")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string WorkingDirectory { get; init; }

    [JsonPropertyName("transcriptPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string TranscriptPath { get; init; }
}
