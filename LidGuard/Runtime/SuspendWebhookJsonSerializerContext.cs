using System.Text.Json.Serialization;

namespace LidGuard.Runtime;

[JsonSerializable(typeof(LidGuardWebhookRequest))]
[JsonSerializable(typeof(StopFollowUpWebhookPollResponse))]
[JsonSerializable(typeof(StopFollowUpWebhookStartResponse))]
[JsonSerializable(typeof(SuspendWebhookReason))]
internal sealed partial class SuspendWebhookJsonSerializerContext : JsonSerializerContext
{
}
