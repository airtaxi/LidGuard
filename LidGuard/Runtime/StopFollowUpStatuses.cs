namespace LidGuard.Runtime;

internal static class StopFollowUpStatuses
{
    public const string AwaitingReply = "awaiting-reply";
    public const string Canceled = "canceled";
    public const string Disabled = "disabled";
    public const string PollFailed = "poll-failed";
    public const string ReplyReceived = "reply-received";
    public const string TimedOut = "timed-out";
    public const string WebhookFailed = "webhook-failed";
}
