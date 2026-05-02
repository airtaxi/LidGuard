namespace LidGuard.Notifications.Models;

internal static class LidGuardWebhookEventTypes
{
    public const string PreSuspend = nameof(PreSuspend);
    public const string PostSessionEnd = nameof(PostSessionEnd);

    public static bool IsRecognized(string eventType)
        => string.Equals(eventType, PreSuspend, StringComparison.Ordinal)
            || string.Equals(eventType, PostSessionEnd, StringComparison.Ordinal);
}
