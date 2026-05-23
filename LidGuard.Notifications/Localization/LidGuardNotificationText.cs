using LidGuard.Notifications.Data;
using LidGuard.Notifications.Models;

namespace LidGuard.Notifications.Localization;

internal static class LidGuardNotificationText
{
    private const string DisplayWebhookEventTypeStopFollowUpResourceName = "DisplayWebhookEventTypeStopFollowUp";
    private const string DisplayWebhookEventTypePreSuspendResourceName = "DisplayWebhookEventTypePreSuspend";
    private const string DisplayWebhookEventTypePostSessionEndResourceName = "DisplayWebhookEventTypePostSessionEnd";
    private const string DisplayWebhookReasonAwaitingReplyResourceName = "DisplayWebhookReasonAwaitingReply";
    private const string DisplayWebhookReasonCompletedResourceName = "DisplayWebhookReasonCompleted";
    private const string DisplayWebhookReasonSoftLockedResourceName = "DisplayWebhookReasonSoftLocked";
    private const string DisplayWebhookReasonEmergencyHibernationResourceName = "DisplayWebhookReasonEmergencyHibernation";
    private const string DisplayWebhookReasonSessionEndedResourceName = "DisplayWebhookReasonSessionEnded";
    private const string DisplayWebhookStatusPendingResourceName = "DisplayWebhookStatusPending";
    private const string DisplayWebhookStatusProcessingResourceName = "DisplayWebhookStatusProcessing";
    private const string DisplayWebhookStatusCompletedResourceName = "DisplayWebhookStatusCompleted";
    private const string DisplayWebhookStatusFailedResourceName = "DisplayWebhookStatusFailed";
    public static string AccessTokenLabel => Get(nameof(AccessTokenLabel));
    public static string AccessTokenRequired => Get(nameof(AccessTokenRequired));
    public static string ActiveSessionsLabel => Get(nameof(ActiveSessionsLabel));
    public static string ActiveSubscriptionsLabel => Get(nameof(ActiveSubscriptionsLabel));
    public static string AttemptsLabel => Get(nameof(AttemptsLabel));
    public static string Brand => Get(nameof(Brand));
    public static string BrowserNotSubscribed => Get(nameof(BrowserNotSubscribed));
    public static string BrowserSubscribed => Get(nameof(BrowserSubscribed));
    public static string BrowserUnsubscribed => Get(nameof(BrowserUnsubscribed));
    public static string ClosedLidStopFollowUpWebhookCommandLabel => Get(nameof(ClosedLidStopFollowUpWebhookCommandLabel));
    public static string CommandExamplesTitle => Get(nameof(CommandExamplesTitle));
    public static string CopiedButton => Get(nameof(CopiedButton));
    public static string CopyButton => Get(nameof(CopyButton));
    public static string CopyFailedButton => Get(nameof(CopyFailedButton));
    public static string DashboardTitle => Get(nameof(DashboardTitle));
    public static string DeliveriesLabel => Get(nameof(DeliveriesLabel));
    public static string DetailsLabel => Get(nameof(DetailsLabel));
    public static string EndReasonLabel => Get(nameof(EndReasonLabel));
    public static string EndedLabel => Get(nameof(EndedLabel));
    public static string EventDetailsLoadFailed => Get(nameof(EventDetailsLoadFailed));
    public static string EventsLoadFailed => Get(nameof(EventsLoadFailed));
    public static string EventsLoadMore => Get(nameof(EventsLoadMore));
    public static string EventsLoading => Get(nameof(EventsLoading));
    public static string EventsTitle => Get(nameof(EventsTitle));
    public static string FullLastResponseLabel => Get(nameof(FullLastResponseLabel));
    public static string InvalidAccessToken => Get(nameof(InvalidAccessToken));
    public static string LastActivityLabel => Get(nameof(LastActivityLabel));
    public static string LastErrorLabel => Get(nameof(LastErrorLabel));
    public static string LastResponseLabel => Get(nameof(LastResponseLabel));
    public static string LanguageEnglish => Get(nameof(LanguageEnglish));
    public static string LanguageJapanese => Get(nameof(LanguageJapanese));
    public static string LanguageKorean => Get(nameof(LanguageKorean));
    public static string LanguageLabel => Get(nameof(LanguageLabel));
    public static string LanguageSimplifiedChinese => Get(nameof(LanguageSimplifiedChinese));
    public static string LanguageTraditionalChinese => Get(nameof(LanguageTraditionalChinese));
    public static string LidGuardWebhookTitle => Get(nameof(LidGuardWebhookTitle));
    public static string LoginTitle => Get(nameof(LoginTitle));
    public static string NoEventsRecorded => Get(nameof(NoEventsRecorded));
    public static string NoPromptCaptured => Get(nameof(NoPromptCaptured));
    public static string NoResponseCaptured => Get(nameof(NoResponseCaptured));
    public static string NotificationPermissionNotGranted => Get(nameof(NotificationPermissionNotGranted));
    public static string ProcessedLabel => Get(nameof(ProcessedLabel));
    public static string PostSessionEndWebhookCommandLabel => Get(nameof(PostSessionEndWebhookCommandLabel));
    public static string PreSuspendWebhookCommandLabel => Get(nameof(PreSuspendWebhookCommandLabel));
    public static string PublicBaseUrlNotConfigured => Get(nameof(PublicBaseUrlNotConfigured));
    public static string ReadyStatus => Get(nameof(ReadyStatus));
    public static string RememberLoginLabel => Get(nameof(RememberLoginLabel));
    public static string ServiceWorkersUnavailable => Get(nameof(ServiceWorkersUnavailable));
    public static string SignInButton => Get(nameof(SignInButton));
    public static string SignOutNavigation => Get(nameof(SignOutNavigation));
    public static string StartingPromptLabel => Get(nameof(StartingPromptLabel));
    public static string StartedLabel => Get(nameof(StartedLabel));
    public static string StopFollowUpCancelButton => Get(nameof(StopFollowUpCancelButton));
    public static string StopFollowUpCancelHelp => Get(nameof(StopFollowUpCancelHelp));
    public static string StopFollowUpCancelSucceededMessage => Get(nameof(StopFollowUpCancelSucceededMessage));
    public static string StopFollowUpDeadlineLabel => Get(nameof(StopFollowUpDeadlineLabel));
    public static string StopFollowUpExtendButton => Get(nameof(StopFollowUpExtendButton));
    public static string StopFollowUpExtendHelp => Get(nameof(StopFollowUpExtendHelp));
    public static string StopFollowUpExtendLabel => Get(nameof(StopFollowUpExtendLabel));
    public static string StopFollowUpExtendLimitReachedMessage => Get(nameof(StopFollowUpExtendLimitReachedMessage));
    public static string StopFollowUpExtendMinutesLabel => Get(nameof(StopFollowUpExtendMinutesLabel));
    public static string StopFollowUpExtendSendingMessage => Get(nameof(StopFollowUpExtendSendingMessage));
    public static string StopFollowUpExtendSucceededMessage => Get(nameof(StopFollowUpExtendSucceededMessage));
    public static string StopFollowUpMaximumDeadlineLabel => Get(nameof(StopFollowUpMaximumDeadlineLabel));
    public static string StopFollowUpReplyButton => Get(nameof(StopFollowUpReplyButton));
    public static string StopFollowUpReplyAwaitingConsumptionMessage => Get(nameof(StopFollowUpReplyAwaitingConsumptionMessage));
    public static string StopFollowUpReplyConsumedMessage => Get(nameof(StopFollowUpReplyConsumedMessage));
    public static string StopFollowUpReplyHelp => Get(nameof(StopFollowUpReplyHelp));
    public static string StopFollowUpReplyLabel => Get(nameof(StopFollowUpReplyLabel));
    public static string StopFollowUpReplyCollectedAtLabel => Get(nameof(StopFollowUpReplyCollectedAtLabel));
    public static string StopFollowUpReplyPlaceholder => Get(nameof(StopFollowUpReplyPlaceholder));
    public static string StopFollowUpReplyRequiredMessage => Get(nameof(StopFollowUpReplyRequiredMessage));
    public static string StopFollowUpReplySendingMessage => Get(nameof(StopFollowUpReplySendingMessage));
    public static string StopFollowUpReplySentAtLabel => Get(nameof(StopFollowUpReplySentAtLabel));
    public static string StopFollowUpSectionLabel => Get(nameof(StopFollowUpSectionLabel));
    public static string StopFollowUpStatusLabel => Get(nameof(StopFollowUpStatusLabel));
    public static string SubscribeBrowserButton => Get(nameof(SubscribeBrowserButton));
    public static string SubscriptionFailed => Get(nameof(SubscriptionFailed));
    public static string ThemeDarkMode => Get(nameof(ThemeDarkMode));
    public static string ThemeLightMode => Get(nameof(ThemeLightMode));
    public static string ThemeSwitchToFormat => Get(nameof(ThemeSwitchTo));
    public static string TranscriptLabel => Get(nameof(TranscriptLabel));
    public static string UnsubscribeButton => Get(nameof(UnsubscribeButton));
    public static string UnsubscribeFailed => Get(nameof(UnsubscribeFailed));
    public static string UserInterfaceCultureLabel => Get(nameof(UserInterfaceCultureLabel));
    public static string VapidPublicKeyLoadFailed => Get(nameof(VapidPublicKeyLoadFailed));
    public static string WebPushUnavailable => Get(nameof(WebPushUnavailable));
    public static string WebhookEventsHeading => Get(nameof(WebhookEventsHeading));
    public static string WebhookIdLabel => Get(nameof(WebhookIdLabel));
    public static string WorkingDirectoryLabel => Get(nameof(WorkingDirectoryLabel));

    public static string RecentEventsFormat => Get(nameof(RecentEvents));

    public static string DeliverySummary(int successCount, int permanentFailureCount, int transientFailureCount)
        => Format(nameof(DeliverySummary), successCount, permanentFailureCount, transientFailureCount);

    public static string PushActiveSessionsRemaining(int activeSessionCount)
        => Format(nameof(PushActiveSessionsRemaining), activeSessionCount);

    public static string PushEndReason(string endReason)
        => Format(nameof(PushEndReason), endReason);

    public static string PushInputPrompt(string inputPromptPreview)
        => Format(nameof(PushInputPrompt), inputPromptPreview);

    public static string PushLastResponse(string lastResponsePreview)
        => Format(nameof(PushLastResponse), lastResponsePreview);

    public static string PushSession(string sessionIdentifier)
        => Format(nameof(PushSession), sessionIdentifier);

    public static string PushSoftLockedSessionCount(int softLockedSessionCount)
        => Format(nameof(PushSoftLockedSessionCount), softLockedSessionCount);

    public static string RecentEvents(int eventCount)
        => Format(nameof(RecentEvents), eventCount);

    public static string SoftLockedSessionCount(int softLockedSessionCount)
        => Format(nameof(SoftLockedSessionCount), softLockedSessionCount);

    public static string StopFollowUpExtendValidationMessage(int minimumMinutes, int maximumMinutes)
        => Format(nameof(StopFollowUpExtendValidationMessage), minimumMinutes, maximumMinutes);

    public static string StopFollowUpProviderHookTimeoutRemaining(int remainingSeconds)
        => Format(nameof(StopFollowUpProviderHookTimeoutRemaining), remainingSeconds);

    public static string ThemeSwitchTo(string modeLabel)
        => Format(nameof(ThemeSwitchTo), modeLabel);

    public static string DisplayWebhookEventType(string eventType)
        => eventType switch
        {
            LidGuardWebhookEventTypes.StopFollowUp => Get(DisplayWebhookEventTypeStopFollowUpResourceName),
            LidGuardWebhookEventTypes.PreSuspend => Get(DisplayWebhookEventTypePreSuspendResourceName),
            LidGuardWebhookEventTypes.PostSessionEnd => Get(DisplayWebhookEventTypePostSessionEndResourceName),
            _ => string.IsNullOrWhiteSpace(eventType) ? "-" : eventType
        };

    public static string DisplayWebhookReason(string reason)
        => reason switch
        {
            LidGuardWebhookReasons.AwaitingReply => Get(DisplayWebhookReasonAwaitingReplyResourceName),
            LidGuardWebhookReasons.Completed => Get(DisplayWebhookReasonCompletedResourceName),
            LidGuardWebhookReasons.SoftLocked => Get(DisplayWebhookReasonSoftLockedResourceName),
            LidGuardWebhookReasons.EmergencyHibernation => Get(DisplayWebhookReasonEmergencyHibernationResourceName),
            LidGuardWebhookReasons.SessionEnded => Get(DisplayWebhookReasonSessionEndedResourceName),
            _ => string.IsNullOrWhiteSpace(reason) ? "-" : reason
        };

    public static string DisplayWebhookStatus(string status)
        => status switch
        {
            WebhookEventStatuses.Pending => Get(DisplayWebhookStatusPendingResourceName),
            WebhookEventStatuses.Processing => Get(DisplayWebhookStatusProcessingResourceName),
            WebhookEventStatuses.Completed => Get(DisplayWebhookStatusCompletedResourceName),
            WebhookEventStatuses.Failed => Get(DisplayWebhookStatusFailedResourceName),
            _ => string.IsNullOrWhiteSpace(status) ? "-" : status
        };

    public static string PushBodyCompleted => Get(nameof(PushBodyCompleted));
    public static string PushBodyEmergencyHibernation => Get(nameof(PushBodyEmergencyHibernation));
    public static string PushBodyFallback => Get(nameof(PushBodyFallback));
    public static string PushBodyStopFollowUp => Get(nameof(PushBodyStopFollowUp));
    public static string PushPostSessionEndStatus => Get(nameof(PushPostSessionEndStatus));
    public static string PushPreSuspendSessionEndStatus => Get(nameof(PushPreSuspendSessionEndStatus));
    public static string PushProviderFallback => Get(nameof(PushProviderFallback));
    public static string PushSessionFallback => Get(nameof(PushSessionFallback));
    public static string PushSoftLockedAll => Get(nameof(PushSoftLockedAll));
    public static string PushStopFollowUpStatus => Get(nameof(PushStopFollowUpStatus));
    public static string PushTitleCompleted => Get(nameof(PushTitleCompleted));
    public static string PushTitleEmergencyHibernation => Get(nameof(PushTitleEmergencyHibernation));
    public static string PushTitleFallback => Get(nameof(PushTitleFallback));
    public static string PushTitlePostSessionEnd => Get(nameof(PushTitlePostSessionEnd));
    public static string PushTitleStopFollowUp => Get(nameof(PushTitleStopFollowUp));
    public static string PushTitleSoftLocked => Get(nameof(PushTitleSoftLocked));

    private static string Get(string name) => LocalizationService.GetString(name);

    private static string Format(string name, params object[] arguments)
        => LocalizationService.GetFormattedString(name, arguments);

    public static string DisplayStopFollowUpStatus(string status)
        => status switch
        {
            StopFollowUpRequestStatuses.Pending => Get("DisplayStopFollowUpStatusPending"),
            StopFollowUpRequestStatuses.Answered => Get("DisplayStopFollowUpStatusAnswered"),
            StopFollowUpRequestStatuses.Expired => Get("DisplayStopFollowUpStatusExpired"),
            StopFollowUpRequestStatuses.Canceled => Get("DisplayStopFollowUpStatusCanceled"),
            _ => string.IsNullOrWhiteSpace(status) ? "-" : status
        };
}
