using LidGuard.Notifications.Data;
using LidGuard.Notifications.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LidGuard.Notifications.Pages;

internal sealed class EventsModel(WebhookEventStore webhookEventStore) : PageModel
{
    public IReadOnlyList<WebhookEventSummary> Events { get; private set; } = [];

    [TempData]
    public string EventMessage { get; set; } = string.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken)
        => Events = await webhookEventStore.ListRecentAsync(100, cancellationToken);

    public async Task<IActionResult> OnPostReplyAsync(string publicIdentifier, string reply, CancellationToken cancellationToken)
    {
        var trimmedReply = reply?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedReply))
        {
            EventMessage = LidGuardNotificationText.StopFollowUpReplyRequiredMessage;
            return Redirect($"/events#followup-{publicIdentifier}");
        }

        var submissionResult = await webhookEventStore.SubmitStopFollowUpReplyAsync(publicIdentifier, trimmedReply, cancellationToken);
        EventMessage = submissionResult.Message;
        return Redirect($"/events#followup-{publicIdentifier}");
    }

    public async Task<IActionResult> OnPostCancelAsync(string publicIdentifier, CancellationToken cancellationToken)
    {
        var cancellationResult = await webhookEventStore.CancelStopFollowUpAsync(publicIdentifier, cancellationToken);
        EventMessage = cancellationResult.Succeeded ? LidGuardNotificationText.StopFollowUpCancelSucceededMessage : cancellationResult.Message;
        return Redirect($"/events#followup-{publicIdentifier}");
    }
}
