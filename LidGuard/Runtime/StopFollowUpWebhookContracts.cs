using System.Text.Json.Serialization;

namespace LidGuard.Runtime;

internal sealed class StopFollowUpWebhookStartResponse
{
    [JsonPropertyName("followUpRequestIdentifier")]
    public string FollowUpRequestIdentifier { get; init; } = string.Empty;

    [JsonPropertyName("replyPollUrl")]
    public string ReplyPollUrl { get; init; } = string.Empty;

    [JsonPropertyName("expiresAtUtc")]
    public DateTimeOffset? ExpiresAtUtc { get; init; }
}

internal sealed class StopFollowUpWebhookPollResponse
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("reply")]
    public string Reply { get; init; } = string.Empty;
}

internal sealed class StopFollowUpWebhookAwaitResult
{
    public bool ReplyReceived { get; init; }

    public string Reply { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}
