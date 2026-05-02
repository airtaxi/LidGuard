using LidGuard.Notifications.Data;

namespace LidGuard.Notifications.Models;

internal static class PushNotificationMessageFactory
{
    public static PushNotificationMessage Create(PendingWebhookEvent webhookEvent, string publicBaseUrl)
    {
        var notificationUrl = string.IsNullOrWhiteSpace(publicBaseUrl) ? "/events" : $"{publicBaseUrl.TrimEnd('/')}/events";
        return new PushNotificationMessage
        {
            Title = CreateTitle(webhookEvent.Reason),
            Body = CreateBody(webhookEvent),
            Url = notificationUrl,
            Tag = $"lidguard-{webhookEvent.Reason.ToLowerInvariant()}"
        };
    }

    private static string CreateTitle(string reason)
        => reason switch
        {
            LidGuardWebhookReasons.Completed => "LidGuard session completed",
            LidGuardWebhookReasons.SoftLocked => "LidGuard sessions are waiting",
            LidGuardWebhookReasons.EmergencyHibernation => "LidGuard emergency hibernation",
            _ => "LidGuard suspend event"
        };

    private static string CreateBody(PendingWebhookEvent webhookEvent)
        => webhookEvent.Reason switch
        {
            LidGuardWebhookReasons.Completed => "The last active session ended and LidGuard is preparing the configured suspend flow.",
            LidGuardWebhookReasons.SoftLocked => CreateSoftLockedBody(webhookEvent.SoftLockedSessionCount),
            LidGuardWebhookReasons.EmergencyHibernation => "The emergency thermal threshold was reached and hibernation was requested immediately.",
            _ => "LidGuard received a pre-suspend webhook event."
        };

    private static string CreateSoftLockedBody(int? softLockedSessionCount)
    {
        if (softLockedSessionCount is null) return "All remaining sessions are soft-locked and LidGuard is preparing the configured suspend flow.";

        return $"{softLockedSessionCount.Value} session(s) are soft-locked and LidGuard is preparing the configured suspend flow.";
    }
}
