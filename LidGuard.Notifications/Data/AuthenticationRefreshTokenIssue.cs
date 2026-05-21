namespace LidGuard.Notifications.Data;

internal sealed record AuthenticationRefreshTokenIssue(
    string Token,
    DateTimeOffset ExpiresAtUtc);
