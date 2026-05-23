namespace LidGuard.Notifications.Data;

internal sealed record StopFollowUpActionResult(bool Succeeded, bool Extended, string Status, string Message, DateTimeOffset? DeadlineAtUtc, DateTimeOffset? MaximumDeadlineAtUtc, DateTimeOffset? ConsumedAtUtc);
