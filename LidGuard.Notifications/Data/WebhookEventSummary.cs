namespace LidGuard.Notifications.Data;

public sealed record WebhookEventSummary(
    long WebhookEventIdentifier,
    string Reason,
    int? SoftLockedSessionCount,
    DateTimeOffset ReceivedAtUtc,
    DateTimeOffset? ProcessedAtUtc,
    string Status,
    int AttemptCount,
    int DeliveryCount,
    int SuccessCount,
    int PermanentFailureCount,
    int TransientFailureCount,
    string? LastError);
