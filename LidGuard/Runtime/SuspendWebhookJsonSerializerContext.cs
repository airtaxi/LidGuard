using System.Text.Json.Serialization;

namespace LidGuard.Runtime;

[JsonSerializable(typeof(SuspendWebhookRequest))]
[JsonSerializable(typeof(SuspendWebhookReason))]
internal sealed partial class SuspendWebhookJsonSerializerContext : JsonSerializerContext
{
}
