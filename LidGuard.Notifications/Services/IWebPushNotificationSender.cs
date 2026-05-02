using LidGuard.Notifications.Data;
using LidGuard.Notifications.Models;

namespace LidGuard.Notifications.Services;

internal interface IWebPushNotificationSender
{
    Task<PushNotificationSendResult> SendAsync(
        ActivePushSubscription subscription,
        PushNotificationMessage message,
        CancellationToken cancellationToken);
}
