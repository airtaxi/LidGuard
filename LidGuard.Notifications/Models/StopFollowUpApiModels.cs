namespace LidGuard.Notifications.Models;

internal sealed class StopFollowUpReplyRequest
{
    public string? Reply { get; init; }
}

internal sealed class StopFollowUpWebhookAcceptedResponse
{
    public string FollowUpRequestIdentifier { get; init; } = string.Empty;

    public string ReplyPollUrl { get; init; } = string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; init; }
}

internal sealed class StopFollowUpPollResponse
{
    public string Status { get; init; } = string.Empty;

    public string? Reply { get; init; }
}
