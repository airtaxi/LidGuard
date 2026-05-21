using System.Globalization;
using LidGuard.Notifications.Data;
using LidGuard.Notifications.Localization;

namespace LidGuard.Notifications.Models;

internal static class PushNotificationMessageFactory
{
    public static PushNotificationMessage Create(PendingWebhookEvent webhookEvent, string publicBaseUrl)
    {
        if (!TryCreateWebhookCultureInfo(webhookEvent.UserInterfaceCulture, out var cultureInfo)) return CreateCore(webhookEvent, publicBaseUrl);

        var previousCultureInfo = CultureInfo.CurrentCulture;
        var previousUserInterfaceCultureInfo = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = cultureInfo;
            CultureInfo.CurrentUICulture = cultureInfo;
            return CreateCore(webhookEvent, publicBaseUrl);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCultureInfo;
            CultureInfo.CurrentUICulture = previousUserInterfaceCultureInfo;
        }
    }

    private static PushNotificationMessage CreateCore(PendingWebhookEvent webhookEvent, string publicBaseUrl)
    {
        var notificationUrl = CreateNotificationUrl(webhookEvent, publicBaseUrl);
        return new PushNotificationMessage
        {
            Title = CreateTitle(webhookEvent),
            Body = CreateBody(webhookEvent),
            Url = notificationUrl,
            Tag = $"lidguard-{webhookEvent.EventType.ToLowerInvariant()}-{webhookEvent.Reason.ToLowerInvariant()}"
        };
    }

    private static bool TryCreateWebhookCultureInfo(string? userInterfaceCulture, out CultureInfo cultureInfo)
    {
        cultureInfo = CultureInfo.InvariantCulture;
        if (string.IsNullOrWhiteSpace(userInterfaceCulture)) return false;
        if (!NotificationUserInterfaceCultureConfiguration.TryCreateCultureInfo(userInterfaceCulture, out var resolvedCultureInfo, out _)) return false;
        if (string.IsNullOrWhiteSpace(resolvedCultureInfo.Name)) return false;

        cultureInfo = resolvedCultureInfo;
        return true;
    }

    private static string CreateTitle(PendingWebhookEvent webhookEvent)
    {
        if (webhookEvent.EventType.Equals(LidGuardWebhookEventTypes.StopFollowUp, StringComparison.Ordinal))
            return LidGuardNotificationText.PushTitleStopFollowUp;
        if (webhookEvent.EventType.Equals(LidGuardWebhookEventTypes.PostSessionEnd, StringComparison.Ordinal)) return LidGuardNotificationText.PushTitlePostSessionEnd;

        return webhookEvent.Reason switch
        {
            LidGuardWebhookReasons.Completed => LidGuardNotificationText.PushTitleCompleted,
            LidGuardWebhookReasons.SoftLocked => LidGuardNotificationText.PushTitleSoftLocked,
            LidGuardWebhookReasons.EmergencyHibernation => LidGuardNotificationText.PushTitleEmergencyHibernation,
            _ => LidGuardNotificationText.PushTitleFallback
        };
    }

    private static string CreateBody(PendingWebhookEvent webhookEvent)
    {
        if (webhookEvent.EventType.Equals(LidGuardWebhookEventTypes.StopFollowUp, StringComparison.Ordinal))
            return CreateStopFollowUpBody(webhookEvent);
        if (webhookEvent.EventType.Equals(LidGuardWebhookEventTypes.PostSessionEnd, StringComparison.Ordinal)) return CreatePostSessionEndBody(webhookEvent);

        return CreatePreSuspendBody(webhookEvent);
    }

    private static string CreatePreSuspendBody(PendingWebhookEvent webhookEvent)
    {
        var baseBody = webhookEvent.Reason switch
        {
            LidGuardWebhookReasons.Completed => LidGuardNotificationText.PushBodyCompleted,
            LidGuardWebhookReasons.SoftLocked => CreateSoftLockedBody(webhookEvent.SoftLockedSessionCount),
            LidGuardWebhookReasons.EmergencyHibernation => LidGuardNotificationText.PushBodyEmergencyHibernation,
            _ => LidGuardNotificationText.PushBodyFallback
        };

        return AppendSessionEndDetails(baseBody, webhookEvent);
    }

    private static string CreatePostSessionEndBody(PendingWebhookEvent webhookEvent)
        => CreateSessionEndDetails(webhookEvent, LidGuardNotificationText.PushPostSessionEndStatus);

    private static string AppendSessionEndDetails(string baseBody, PendingWebhookEvent webhookEvent)
    {
        if (string.IsNullOrWhiteSpace(webhookEvent.SessionIdentifier)) return baseBody;

        return $"{baseBody} {CreateSessionEndDetails(webhookEvent, LidGuardNotificationText.PushPreSuspendSessionEndStatus)}";
    }

    private static string CreateSessionEndDetails(PendingWebhookEvent webhookEvent, string statusText)
    {
        var providerText = string.IsNullOrWhiteSpace(webhookEvent.ProviderName)
            ? webhookEvent.Provider
            : $"{webhookEvent.Provider}:{webhookEvent.ProviderName}";
        if (string.IsNullOrWhiteSpace(providerText)) providerText = LidGuardNotificationText.PushProviderFallback;

        var sessionText = string.IsNullOrWhiteSpace(webhookEvent.SessionIdentifier)
            ? LidGuardNotificationText.PushSessionFallback
            : LidGuardNotificationText.PushSession(webhookEvent.SessionIdentifier);
        var details = new List<string> { $"{providerText} {sessionText} {statusText}" };
        if (!string.IsNullOrWhiteSpace(webhookEvent.EndReason)) details.Add(LidGuardNotificationText.PushEndReason(webhookEvent.EndReason));
        if (webhookEvent.ActiveSessionCount is not null) details.Add(LidGuardNotificationText.PushActiveSessionsRemaining(webhookEvent.ActiveSessionCount.Value));
        if (!string.IsNullOrWhiteSpace(webhookEvent.InputPromptPreview)) details.Add(LidGuardNotificationText.PushInputPrompt(webhookEvent.InputPromptPreview));
        var lastResponsePreview = WebhookTextPreview.Create(webhookEvent.LastResponse);
        if (!string.IsNullOrWhiteSpace(lastResponsePreview)) details.Add(LidGuardNotificationText.PushLastResponse(lastResponsePreview));
        return string.Join(" ", details);
    }

    private static string CreateSoftLockedBody(int? softLockedSessionCount)
    {
        if (softLockedSessionCount is null) return LidGuardNotificationText.PushSoftLockedAll;

        return LidGuardNotificationText.PushSoftLockedSessionCount(softLockedSessionCount.Value);
    }

    private static string CreateStopFollowUpBody(PendingWebhookEvent webhookEvent)
        => CreateSessionEndDetails(webhookEvent, LidGuardNotificationText.PushStopFollowUpStatus) + " " + LidGuardNotificationText.PushBodyStopFollowUp;

    private static string CreateNotificationUrl(PendingWebhookEvent webhookEvent, string publicBaseUrl)
    {
        var baseEventsUrl = string.IsNullOrWhiteSpace(publicBaseUrl) ? "/events" : $"{publicBaseUrl.TrimEnd('/')}/events";
        if (!webhookEvent.EventType.Equals(LidGuardWebhookEventTypes.StopFollowUp, StringComparison.Ordinal)) return baseEventsUrl;
        if (string.IsNullOrWhiteSpace(webhookEvent.StopFollowUpPublicIdentifier)) return baseEventsUrl;
        return $"{baseEventsUrl}#followup-{webhookEvent.StopFollowUpPublicIdentifier}";
    }
}
