namespace LidGuard.Notifications.Data;

public sealed record WebhookEventListPage(IReadOnlyList<WebhookEventListItem> Events, bool HasMore, long? NextBeforeWebhookEventIdentifier);
