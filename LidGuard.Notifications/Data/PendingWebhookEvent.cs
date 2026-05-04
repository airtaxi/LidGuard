namespace LidGuard.Notifications.Data;

internal sealed record PendingWebhookEvent(
    long WebhookEventIdentifier,
    string EventType,
    string Reason,
    string? UserInterfaceCulture,
    int? SoftLockedSessionCount,
    string? Provider,
    string? ProviderName,
    string? SessionIdentifier,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? LastActivityAtUtc,
    DateTimeOffset? EndedAtUtc,
    string? EndReason,
    int? ActiveSessionCount,
    string? InputPromptPreview,
    string? LastResponse,
    string? WorkingDirectory,
    string? TranscriptPath,
    DateTimeOffset ReceivedAtUtc,
    int AttemptCount);
