using LidGuard.Notifications.Data;
using LidGuard.Notifications.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace LidGuard.Notifications.Pages;

internal sealed class EventsModel(WebhookEventStore webhookEventStore) : PageModel
{
    private const int EventPageSize = 10;
    private const int StopFollowUpConsumptionPollCycleCount = 4;
    private static readonly TimeSpan s_stopFollowUpConsumptionPollInterval = TimeSpan.FromSeconds(1);

    public WebhookEventListPage EventPage { get; private set; } = new([], false, null);

    [TempData]
    public string EventMessage { get; set; } = string.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken)
        => EventPage = await webhookEventStore.ListRecentPageAsync(EventPageSize, null, cancellationToken);

    public async Task<IActionResult> OnGetMoreAsync(long? beforeWebhookEventIdentifier, CancellationToken cancellationToken)
    {
        if (beforeWebhookEventIdentifier is null || beforeWebhookEventIdentifier.Value <= 0) return BadRequest(LidGuardNotificationText.EventsLoadFailed);

        var eventPage = await webhookEventStore.ListRecentPageAsync(EventPageSize, beforeWebhookEventIdentifier, cancellationToken);
        return Partial("_WebhookEventCardList", eventPage);
    }

    public async Task<IActionResult> OnGetDetailsAsync(long webhookEventIdentifier, CancellationToken cancellationToken)
    {
        if (webhookEventIdentifier <= 0) return BadRequest();

        var eventDetails = await webhookEventStore.GetDetailsAsync(webhookEventIdentifier, cancellationToken);
        if (eventDetails is null) return NotFound(LidGuardNotificationText.EventDetailsLoadFailed);

        return Partial("_WebhookEventDetails", eventDetails);
    }

    public async Task<IActionResult> OnPostReplyAsync(string publicIdentifier, string reply, CancellationToken cancellationToken)
    {
        var trimmedReply = reply?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedReply))
        {
            EventMessage = LidGuardNotificationText.StopFollowUpReplyRequiredMessage;
            return Redirect($"/events#followup-{publicIdentifier}");
        }

        var submissionResult = await webhookEventStore.SubmitStopFollowUpReplyAsync(publicIdentifier, trimmedReply, cancellationToken);
        if (!submissionResult.Succeeded)
        {
            EventMessage = submissionResult.Message;
            return Redirect($"/events#followup-{publicIdentifier}");
        }

        var wasConsumed = await webhookEventStore.WaitForStopFollowUpConsumptionAsync(publicIdentifier, StopFollowUpConsumptionPollCycleCount, s_stopFollowUpConsumptionPollInterval, cancellationToken);
        EventMessage = wasConsumed ? LidGuardNotificationText.StopFollowUpReplyConsumedMessage : LidGuardNotificationText.StopFollowUpReplyAwaitingConsumptionMessage;
        return Redirect($"/events#followup-{publicIdentifier}");
    }

    public async Task<IActionResult> OnPostCancelAsync(string publicIdentifier, CancellationToken cancellationToken)
    {
        var cancellationResult = await webhookEventStore.CancelStopFollowUpAsync(publicIdentifier, cancellationToken);
        EventMessage = cancellationResult.Succeeded ? LidGuardNotificationText.StopFollowUpCancelSucceededMessage : cancellationResult.Message;
        return Redirect($"/events#followup-{publicIdentifier}");
    }

    private PartialViewResult Partial<TModel>(string viewName, TModel model)
        => new()
        {
            ViewName = viewName,
            ViewData = new ViewDataDictionary<TModel>(ViewData, model)
        };
}
