namespace LidGuard.Notifications.Models;

internal sealed class PushSubscriptionChangeRequest
{
    public string? Endpoint { get; init; }

    public PushSubscriptionKeys? Keys { get; init; }
}
