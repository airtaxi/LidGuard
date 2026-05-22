namespace LidGuard.Notifications.Data;

internal sealed record StopFollowUpRequestAcceptedResult(string PublicIdentifier, string PollToken, DateTimeOffset ExpiresAtUtc);
