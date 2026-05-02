using LidGuard.Notifications.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LidGuard.Notifications.Pages;

internal sealed class EventsModel(WebhookEventStore webhookEventStore) : PageModel
{
    public IReadOnlyList<WebhookEventSummary> Events { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
        => Events = await webhookEventStore.ListRecentAsync(100, cancellationToken);
}
