namespace LidGuard.Notifications.Data;

public sealed record WebhookEventListItem(long WebhookEventIdentifier, string EventType, string Reason, int? SoftLockedSessionCount, string? Provider, string? ProviderName, string? SessionIdentifier, string? InputPromptPreview, string? LastAssistantMessagePreview, DateTimeOffset? ReplyDeadlineUtc, DateTimeOffset? StopFollowUpMaximumDeadlineUtc, string? StopFollowUpPublicIdentifier, string? StopFollowUpStatus, string? StopFollowUpReplyPreview, DateTimeOffset ReceivedAtUtc, string Status);
