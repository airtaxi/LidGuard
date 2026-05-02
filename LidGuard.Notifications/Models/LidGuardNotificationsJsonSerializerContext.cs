using System.Text.Json.Serialization;

namespace LidGuard.Notifications.Models;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LidGuardWebhookRequest))]
[JsonSerializable(typeof(PublicKeyResponse))]
[JsonSerializable(typeof(PushNotificationMessage))]
[JsonSerializable(typeof(PushSubscriptionChangeRequest))]
[JsonSerializable(typeof(PushSubscriptionKeys))]
internal sealed partial class LidGuardNotificationsJsonSerializerContext : JsonSerializerContext
{
}
