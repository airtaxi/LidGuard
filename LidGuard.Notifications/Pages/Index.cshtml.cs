using LidGuard.Notifications.Configuration;
using LidGuard.Notifications.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace LidGuard.Notifications.Pages;

internal sealed class IndexModel(
    PushSubscriptionStore subscriptionStore,
    IOptions<LidGuardNotificationsOptions> options) : PageModel
{
    public int ActiveSubscriptionCount { get; private set; }

    public string WebhookUrl { get; private set; } = string.Empty;

    public bool HasPublicBaseUrl { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ActiveSubscriptionCount = await subscriptionStore.CountActiveAsync(cancellationToken);
        HasPublicBaseUrl = !string.IsNullOrWhiteSpace(options.Value.PublicBaseUrl);
        var webhookPath = $"/api/webhooks/lidguard/{options.Value.WebhookSecret}";
        WebhookUrl = HasPublicBaseUrl ? $"{options.Value.PublicBaseUrl}{webhookPath}" : webhookPath;
    }
}
