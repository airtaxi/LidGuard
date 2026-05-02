using LidGuard.Notifications.Configuration;
using LidGuard.Notifications.Data;
using LidGuard.Notifications.Models;
using Microsoft.Extensions.Options;

namespace LidGuard.Notifications.Services;

internal sealed class NotificationDispatchService(
    WebhookEventProcessingSignal processingSignal,
    WebhookEventStore webhookEventStore,
    PushSubscriptionStore subscriptionStore,
    NotificationDeliveryStore deliveryStore,
    IWebPushNotificationSender pushNotificationSender,
    IOptions<LidGuardNotificationsOptions> options,
    ILogger<NotificationDispatchService> logger) : BackgroundService
{
    private const int WebhookEventBatchSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessAvailableEventsAsync(stoppingToken);

            try
            {
                await processingSignal.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task ProcessAvailableEventsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var pendingEvents = await webhookEventStore.ClaimPendingAsync(WebhookEventBatchSize, cancellationToken);
            if (pendingEvents.Count == 0) return;

            foreach (var pendingEvent in pendingEvents) await ProcessEventAsync(pendingEvent, cancellationToken);
        }
    }

    private async Task ProcessEventAsync(PendingWebhookEvent webhookEvent, CancellationToken cancellationToken)
    {
        try
        {
            var subscriptions = await subscriptionStore.ListActiveAsync(cancellationToken);
            var message = PushNotificationMessageFactory.Create(webhookEvent, options.Value.PublicBaseUrl);

            foreach (var subscription in subscriptions)
            {
                var deliveryResult = await pushNotificationSender.SendAsync(subscription, message, cancellationToken);
                await RecordDeliveryAsync(webhookEvent, subscription, deliveryResult, cancellationToken);
            }

            await webhookEventStore.CompleteAsync(webhookEvent.WebhookEventIdentifier, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to process LidGuard webhook event {WebhookEventIdentifier}.", webhookEvent.WebhookEventIdentifier);
            await webhookEventStore.FailAsync(webhookEvent.WebhookEventIdentifier, exception.Message, cancellationToken);
        }
    }

    private async Task RecordDeliveryAsync(
        PendingWebhookEvent webhookEvent,
        ActivePushSubscription subscription,
        PushNotificationSendResult deliveryResult,
        CancellationToken cancellationToken)
    {
        var deliveryStatus = GetDeliveryStatus(deliveryResult.Status);
        await deliveryStore.InsertAsync(
            webhookEvent.WebhookEventIdentifier,
            subscription.SubscriptionIdentifier,
            deliveryStatus,
            deliveryResult.HttpStatusCode,
            deliveryResult.ErrorMessage,
            cancellationToken);

        if (deliveryResult.Status == PushNotificationSendStatus.Succeeded)
        {
            await subscriptionStore.MarkDeliverySucceededAsync(subscription.SubscriptionIdentifier, cancellationToken);
            return;
        }

        if (deliveryResult.Status == PushNotificationSendStatus.PermanentFailure)
        {
            await subscriptionStore.DeactivateForPermanentFailureAsync(subscription.SubscriptionIdentifier, cancellationToken);
            return;
        }

        await subscriptionStore.RecordTransientFailureAsync(subscription.SubscriptionIdentifier, cancellationToken);
    }

    private static string GetDeliveryStatus(PushNotificationSendStatus status)
        => status switch
        {
            PushNotificationSendStatus.Succeeded => DeliveryStatuses.Succeeded,
            PushNotificationSendStatus.PermanentFailure => DeliveryStatuses.PermanentFailure,
            PushNotificationSendStatus.TransientFailure => DeliveryStatuses.TransientFailure,
            _ => DeliveryStatuses.TransientFailure
        };
}
