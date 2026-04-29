using System.Text.Json.Serialization;

namespace LidGuard.Runtime;

[JsonConverter(typeof(JsonStringEnumConverter<SuspendWebhookReason>))]
internal enum SuspendWebhookReason
{
    Completed = 0,
    SoftLocked = 1
}
