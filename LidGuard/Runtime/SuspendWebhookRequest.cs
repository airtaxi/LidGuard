using System.Text.Json.Serialization;

namespace LidGuard.Runtime;

internal sealed class SuspendWebhookRequest
{
    [JsonPropertyName("reason")]
    public SuspendWebhookReason Reason { get; init; }

    [JsonPropertyName("softLockedSessionCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SoftLockedSessionCount { get; init; }
}
