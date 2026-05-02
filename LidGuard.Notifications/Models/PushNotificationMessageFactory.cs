using LidGuard.Notifications.Data;

namespace LidGuard.Notifications.Models;

internal static class PushNotificationMessageFactory
{
    public static PushNotificationMessage Create(PendingWebhookEvent webhookEvent, string publicBaseUrl)
    {
        var notificationUrl = string.IsNullOrWhiteSpace(publicBaseUrl) ? "/events" : $"{publicBaseUrl.TrimEnd('/')}/events";
        return new PushNotificationMessage
        {
            Title = CreateTitle(webhookEvent),
            Body = CreateBody(webhookEvent),
            Url = notificationUrl,
            Tag = $"lidguard-{webhookEvent.EventType.ToLowerInvariant()}-{webhookEvent.Reason.ToLowerInvariant()}"
        };
    }

    private static string CreateTitle(PendingWebhookEvent webhookEvent)
    {
        if (webhookEvent.EventType.Equals(LidGuardWebhookEventTypes.PostSessionEnd, StringComparison.Ordinal)) return "LidGuard session ended";

        return webhookEvent.Reason switch
        {
            LidGuardWebhookReasons.Completed => "LidGuard session completed",
            LidGuardWebhookReasons.SoftLocked => "LidGuard sessions are waiting",
            LidGuardWebhookReasons.EmergencyHibernation => "LidGuard emergency hibernation",
            _ => "LidGuard suspend event"
        };
    }

    private static string CreateBody(PendingWebhookEvent webhookEvent)
    {
        if (webhookEvent.EventType.Equals(LidGuardWebhookEventTypes.PostSessionEnd, StringComparison.Ordinal)) return CreatePostSessionEndBody(webhookEvent);

        return CreatePreSuspendBody(webhookEvent);
    }

    private static string CreatePreSuspendBody(PendingWebhookEvent webhookEvent)
        => webhookEvent.Reason switch
        {
            LidGuardWebhookReasons.Completed => "The last active session ended and LidGuard is preparing the configured suspend flow.",
            LidGuardWebhookReasons.SoftLocked => CreateSoftLockedBody(webhookEvent.SoftLockedSessionCount),
            LidGuardWebhookReasons.EmergencyHibernation => "The emergency thermal threshold was reached and hibernation was requested immediately.",
            _ => "LidGuard received a pre-suspend webhook event."
        };

    private static string CreatePostSessionEndBody(PendingWebhookEvent webhookEvent)
    {
        var providerText = string.IsNullOrWhiteSpace(webhookEvent.ProviderName)
            ? webhookEvent.Provider
            : $"{webhookEvent.Provider}:{webhookEvent.ProviderName}";
        if (string.IsNullOrWhiteSpace(providerText)) providerText = "Provider";

        var sessionText = string.IsNullOrWhiteSpace(webhookEvent.SessionIdentifier)
            ? "session"
            : $"session {webhookEvent.SessionIdentifier}";
        var endReasonText = string.IsNullOrWhiteSpace(webhookEvent.EndReason)
            ? string.Empty
            : $" Reason: {webhookEvent.EndReason}.";
        var activeSessionText = webhookEvent.ActiveSessionCount is null
            ? string.Empty
            : $" Active sessions remaining: {webhookEvent.ActiveSessionCount.Value}.";
        return $"{providerText} {sessionText} ended normally.{endReasonText}{activeSessionText}";
    }

    private static string CreateSoftLockedBody(int? softLockedSessionCount)
    {
        if (softLockedSessionCount is null) return "All remaining sessions are soft-locked and LidGuard is preparing the configured suspend flow.";

        return $"{softLockedSessionCount.Value} session(s) are soft-locked and LidGuard is preparing the configured suspend flow.";
    }
}
