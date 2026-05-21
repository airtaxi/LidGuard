using System.Text.Json.Serialization;

namespace LidGuard.Hooks;

internal sealed class StopHookContinuationDecisionOutput
{
    [JsonPropertyName("decision")]
    public string Decision { get; init; } = "block";

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;
}
