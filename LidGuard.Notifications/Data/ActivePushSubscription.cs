namespace LidGuard.Notifications.Data;

internal sealed record ActivePushSubscription(
    long SubscriptionIdentifier,
    string Endpoint,
    string P256dh,
    string Auth);
