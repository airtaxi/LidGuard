namespace LidGuard.Notifications.Models;

internal sealed class LidGuardWebhookRequest
{
    public string? Reason { get; init; }

    public int? SoftLockedSessionCount { get; init; }
}
