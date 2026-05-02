using System.Text.Json.Serialization;

namespace LidGuard.Runtime;

[JsonSerializable(typeof(LidGuardWebhookRequest))]
[JsonSerializable(typeof(SuspendWebhookReason))]
internal sealed partial class SuspendWebhookJsonSerializerContext : JsonSerializerContext
{
}
