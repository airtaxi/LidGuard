using System.Net;
using System.Text.Json;
using LidGuard.Notifications.Configuration;
using LidGuard.Notifications.Data;
using LidGuard.Notifications.Models;
using Microsoft.Extensions.Options;
using WebPush;

namespace LidGuard.Notifications.Services;

internal sealed class ClosureOpenSourceWebPushNotificationSender(
    WebPushClient webPushClient,
    IOptions<LidGuardNotificationsOptions> options,
    ILogger<ClosureOpenSourceWebPushNotificationSender> logger) : IWebPushNotificationSender
{
    public async Task<PushNotificationSendResult> SendAsync(
        ActivePushSubscription subscription,
        PushNotificationMessage message,
        CancellationToken cancellationToken)
    {
        var pushSubscription = new PushSubscription(subscription.Endpoint, subscription.P256dh, subscription.Auth);
        var webPushOptions = new WebPushOptions
        {
            VapidDetails = new VapidDetails(options.Value.VapidSubject, options.Value.VapidPublicKey, options.Value.VapidPrivateKey),
            TTL = 300,
            Urgency = Urgency.High
        };
        var payload = JsonSerializer.Serialize(message, LidGuardNotificationsJsonSerializerContext.Default.PushNotificationMessage);

        try
        {
            await webPushClient.SendNotificationAsync(pushSubscription, payload, webPushOptions, cancellationToken);
            return PushNotificationSendResult.Succeeded();
        }
        catch (WebPushException exception) when (exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            logger.LogInformation("Deactivating expired Web Push subscription {Endpoint}.", subscription.Endpoint);
            return PushNotificationSendResult.PermanentFailure((int)exception.StatusCode, exception.Message);
        }
        catch (WebPushException exception)
        {
            logger.LogWarning(exception, "Web Push delivery failed for {Endpoint}.", subscription.Endpoint);
            return PushNotificationSendResult.TransientFailure((int)exception.StatusCode, exception.Message);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Web Push delivery request failed for {Endpoint}.", subscription.Endpoint);
            return PushNotificationSendResult.TransientFailure(null, exception.Message);
        }
    }
}
