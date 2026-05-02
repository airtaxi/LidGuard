namespace LidGuard.Notifications.Data;

internal sealed record PendingWebhookEvent(
    long WebhookEventIdentifier,
    string Reason,
    int? SoftLockedSessionCount,
    DateTimeOffset ReceivedAtUtc,
    int AttemptCount);
