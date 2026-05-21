using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using LidGuard.Notifications.Configuration;
using LidGuard.Notifications.Data;
using LidGuard.Notifications.Localization;
using LidGuard.Notifications.Models;
using LidGuard.Notifications.Security;
using Microsoft.Extensions.Options;

namespace LidGuard.Notifications.Services;

internal static class LidGuardNotificationApiEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/healthz", () => Results.Text("ok", "text/plain"));

        app.MapGet("/api/push/public-key", async (
            IOptions<LidGuardNotificationsOptions> options,
            HttpResponse response,
            CancellationToken cancellationToken) =>
        {
            var publicKeyResponse = new PublicKeyResponse { PublicKey = options.Value.VapidPublicKey };
            await WriteJsonAsync(response, publicKeyResponse, LidGuardNotificationsJsonSerializerContext.Default.PublicKeyResponse, StatusCodes.Status200OK, cancellationToken);
        });

        app.MapPost("/api/push/subscriptions", async (
            HttpRequest request,
            HttpResponse response,
            PushSubscriptionStore subscriptionStore,
            CancellationToken cancellationToken) =>
        {
            var subscriptionRequest = await ReadJsonAsync(request, LidGuardNotificationsJsonSerializerContext.Default.PushSubscriptionChangeRequest, cancellationToken);
            if (!TryValidateSubscription(subscriptionRequest, out var endpoint, out var p256dhKey, out var authenticationSecret, out var errorMessage))
            {
                await WriteTextAsync(response, errorMessage, StatusCodes.Status400BadRequest, cancellationToken);
                return;
            }

            await subscriptionStore.UpsertAsync(endpoint, p256dhKey, authenticationSecret, cancellationToken);
            await WriteSubscriptionChangeResponseAsync(response, subscriptionStore, cancellationToken);
        }).RequireAuthorization();

        app.MapDelete("/api/push/subscriptions", async (
            HttpRequest request,
            HttpResponse response,
            PushSubscriptionStore subscriptionStore,
            CancellationToken cancellationToken) =>
        {
            var subscriptionRequest = await ReadJsonAsync(request, LidGuardNotificationsJsonSerializerContext.Default.PushSubscriptionChangeRequest, cancellationToken);
            if (string.IsNullOrWhiteSpace(subscriptionRequest?.Endpoint))
            {
                await WriteTextAsync(response, "Endpoint is required.", StatusCodes.Status400BadRequest, cancellationToken);
                return;
            }

            await subscriptionStore.DeactivateByEndpointAsync(subscriptionRequest.Endpoint, cancellationToken);
            await WriteSubscriptionChangeResponseAsync(response, subscriptionStore, cancellationToken);
        }).RequireAuthorization();

        app.MapPost("/api/webhooks/lidguard/{webhookSecret}", async (
            string webhookSecret,
            HttpRequest request,
            HttpResponse response,
            IOptions<LidGuardNotificationsOptions> options,
            WebhookEventStore webhookEventStore,
            WebhookEventProcessingSignal processingSignal,
            CancellationToken cancellationToken) =>
        {
            if (!SecretVerifier.EqualsConfiguredSecret(options.Value.WebhookSecret, webhookSecret))
            {
                await WriteTextAsync(response, "Not found.", StatusCodes.Status404NotFound, cancellationToken);
                return;
            }

            var webhookRequest = await ReadJsonAsync(request, LidGuardNotificationsJsonSerializerContext.Default.LidGuardWebhookRequest, cancellationToken);
            if (!TryValidateWebhook(
                webhookRequest,
                out var eventType,
                out var reason,
                out var softLockedSessionCount,
                out var replyWaitSeconds,
                out var replyDeadlineUtc,
                out var errorMessage))
            {
                await WriteTextAsync(response, errorMessage, StatusCodes.Status400BadRequest, cancellationToken);
                return;
            }

            var normalizedUserInterfaceCulture = NormalizeWebhookUserInterfaceCulture(webhookRequest?.UserInterfaceCulture);
            if (eventType.Equals(LidGuardWebhookEventTypes.StopFollowUp, StringComparison.Ordinal))
            {
                var stopFollowUpRequestAcceptedResult = await webhookEventStore.InsertStopFollowUpAsync(
                    eventType,
                    reason,
                    normalizedUserInterfaceCulture,
                    softLockedSessionCount,
                    webhookRequest?.Provider?.Trim(),
                    webhookRequest?.ProviderName?.Trim(),
                    webhookRequest?.SessionIdentifier?.Trim(),
                    webhookRequest?.StartedAtUtc,
                    webhookRequest?.LastActivityAtUtc,
                    webhookRequest?.EndedAtUtc,
                    webhookRequest?.EndReason?.Trim(),
                    webhookRequest?.ActiveSessionCount,
                    webhookRequest?.InputPromptPreview?.Trim(),
                    webhookRequest?.LastResponse?.Trim(),
                    replyWaitSeconds!.Value,
                    replyDeadlineUtc!.Value,
                    webhookRequest?.WorkingDirectory?.Trim(),
                    webhookRequest?.TranscriptPath?.Trim(),
                    cancellationToken);
                processingSignal.Signal();
                var pollPath = $"/api/follow-ups/{stopFollowUpRequestAcceptedResult.PublicIdentifier}/poll/{stopFollowUpRequestAcceptedResult.PollToken}";
                await WriteJsonAsync(
                    response,
                    new StopFollowUpWebhookAcceptedResponse
                    {
                        FollowUpRequestIdentifier = stopFollowUpRequestAcceptedResult.PublicIdentifier,
                        ReplyPollUrl = pollPath,
                        ExpiresAtUtc = stopFollowUpRequestAcceptedResult.ExpiresAtUtc
                    },
                    LidGuardNotificationsJsonSerializerContext.Default.StopFollowUpWebhookAcceptedResponse,
                    StatusCodes.Status202Accepted,
                    cancellationToken);
                return;
            }

            await webhookEventStore.InsertAsync(
                eventType,
                reason,
                normalizedUserInterfaceCulture,
                softLockedSessionCount,
                webhookRequest?.Provider?.Trim(),
                webhookRequest?.ProviderName?.Trim(),
                webhookRequest?.SessionIdentifier?.Trim(),
                webhookRequest?.StartedAtUtc,
                webhookRequest?.LastActivityAtUtc,
                webhookRequest?.EndedAtUtc,
                webhookRequest?.EndReason?.Trim(),
                webhookRequest?.ActiveSessionCount,
                webhookRequest?.InputPromptPreview?.Trim(),
                webhookRequest?.LastResponse?.Trim(),
                replyWaitSeconds,
                replyDeadlineUtc,
                webhookRequest?.WorkingDirectory?.Trim(),
                webhookRequest?.TranscriptPath?.Trim(),
                cancellationToken);
            processingSignal.Signal();
            response.StatusCode = StatusCodes.Status202Accepted;
        });

        app.MapPost("/api/follow-ups/{publicIdentifier}/reply", async (
            string publicIdentifier,
            HttpRequest request,
            HttpResponse response,
            WebhookEventStore webhookEventStore,
            CancellationToken cancellationToken) =>
        {
            var replyRequest = await ReadJsonAsync(request, LidGuardNotificationsJsonSerializerContext.Default.StopFollowUpReplyRequest, cancellationToken);
            var reply = replyRequest?.Reply?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(reply))
            {
                await WriteTextAsync(response, "reply is required.", StatusCodes.Status400BadRequest, cancellationToken);
                return;
            }

            var submissionResult = await webhookEventStore.SubmitStopFollowUpReplyAsync(publicIdentifier, reply, cancellationToken);
            if (submissionResult.Succeeded)
            {
                response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            var statusCode = submissionResult.Status switch
            {
                StopFollowUpRequestStatuses.Answered => StatusCodes.Status409Conflict,
                StopFollowUpRequestStatuses.Expired => StatusCodes.Status410Gone,
                StopFollowUpRequestStatuses.Canceled => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status404NotFound
            };
            await WriteTextAsync(response, submissionResult.Message, statusCode, cancellationToken);
        }).RequireAuthorization();

        app.MapPost("/api/follow-ups/{publicIdentifier}/cancel", async (
            string publicIdentifier,
            HttpResponse response,
            WebhookEventStore webhookEventStore,
            CancellationToken cancellationToken) =>
        {
            var cancellationResult = await webhookEventStore.CancelStopFollowUpAsync(publicIdentifier, cancellationToken);
            if (cancellationResult.Succeeded)
            {
                response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            var statusCode = cancellationResult.Status switch
            {
                StopFollowUpRequestStatuses.Answered => StatusCodes.Status409Conflict,
                StopFollowUpRequestStatuses.Expired => StatusCodes.Status410Gone,
                _ => StatusCodes.Status404NotFound
            };
            await WriteTextAsync(response, cancellationResult.Message, statusCode, cancellationToken);
        }).RequireAuthorization();

        app.MapGet("/api/follow-ups/{publicIdentifier}/poll/{pollToken}", async (
            string publicIdentifier,
            string pollToken,
            HttpResponse response,
            WebhookEventStore webhookEventStore,
            CancellationToken cancellationToken) =>
        {
            var pollResponse = await webhookEventStore.GetStopFollowUpPollResponseAsync(publicIdentifier, pollToken, cancellationToken);
            if (pollResponse is null)
            {
                await WriteTextAsync(response, "Not found.", StatusCodes.Status404NotFound, cancellationToken);
                return;
            }

            await WriteJsonAsync(
                response,
                pollResponse,
                LidGuardNotificationsJsonSerializerContext.Default.StopFollowUpPollResponse,
                StatusCodes.Status200OK,
                cancellationToken);
        });
    }

    private static async Task<TValue?> ReadJsonAsync<TValue>(
        HttpRequest request,
        JsonTypeInfo<TValue> jsonTypeInfo,
        CancellationToken cancellationToken)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync(request.Body, jsonTypeInfo, cancellationToken);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static bool TryValidateSubscription(
        PushSubscriptionChangeRequest? request,
        out string endpoint,
        out string p256dhKey,
        out string authenticationSecret,
        out string errorMessage)
    {
        endpoint = request?.Endpoint?.Trim() ?? string.Empty;
        p256dhKey = request?.Keys?.P256dh?.Trim() ?? string.Empty;
        authenticationSecret = request?.Keys?.Auth?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            errorMessage = "Endpoint is required.";
            return false;
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)
            || (endpointUri.Scheme != Uri.UriSchemeHttps && endpointUri.Host != "localhost"))
        {
            errorMessage = "Endpoint must be an absolute HTTPS URL or localhost URL.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(p256dhKey))
        {
            errorMessage = "keys.p256dh is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(authenticationSecret))
        {
            errorMessage = "keys.auth is required.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    private static bool TryValidateWebhook(
        LidGuardWebhookRequest? request,
        out string eventType,
        out string reason,
        out int? softLockedSessionCount,
        out int? replyWaitSeconds,
        out DateTimeOffset? replyDeadlineUtc,
        out string errorMessage)
    {
        eventType = request?.EventType?.Trim() ?? string.Empty;
        reason = request?.Reason?.Trim() ?? string.Empty;
        softLockedSessionCount = request?.SoftLockedSessionCount;
        replyWaitSeconds = request?.ReplyWaitSeconds;
        replyDeadlineUtc = request?.ReplyDeadlineUtc;

        if (!LidGuardWebhookEventTypes.IsRecognized(eventType))
        {
            errorMessage = "eventType is required and must be StopFollowUp, PreSuspend, or PostSessionEnd.";
            return false;
        }

        if (softLockedSessionCount.HasValue && softLockedSessionCount.Value < 0)
        {
            errorMessage = "softLockedSessionCount must be zero or greater when supplied.";
            return false;
        }

        if (eventType.Equals(LidGuardWebhookEventTypes.StopFollowUp, StringComparison.Ordinal))
        {
            if (!LidGuardWebhookReasons.IsRecognizedStopFollowUpReason(reason))
            {
                errorMessage = "reason must be AwaitingReply for StopFollowUp events.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(request?.Provider))
            {
                errorMessage = "provider is required for StopFollowUp events.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(request?.SessionIdentifier))
            {
                errorMessage = "sessionIdentifier is required for StopFollowUp events.";
                return false;
            }

            if (!replyWaitSeconds.HasValue || replyWaitSeconds.Value <= 0)
            {
                errorMessage = "replyWaitSeconds must be an integer greater than 0 for StopFollowUp events.";
                return false;
            }

            if (!replyDeadlineUtc.HasValue)
            {
                errorMessage = "replyDeadlineUtc is required for StopFollowUp events.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        if (eventType.Equals(LidGuardWebhookEventTypes.PreSuspend, StringComparison.Ordinal))
        {
            if (LidGuardWebhookReasons.IsRecognizedPreSuspendReason(reason))
            {
                errorMessage = string.Empty;
                return true;
            }

            errorMessage = "reason must be Completed, SoftLocked, or EmergencyHibernation for PreSuspend events.";
            return false;
        }

        if (!LidGuardWebhookReasons.IsRecognizedPostSessionEndReason(reason))
        {
            errorMessage = "reason must be SessionEnded for PostSessionEnd events.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request?.Provider))
        {
            errorMessage = "provider is required for PostSessionEnd events.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request?.SessionIdentifier))
        {
            errorMessage = "sessionIdentifier is required for PostSessionEnd events.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    private static string? NormalizeWebhookUserInterfaceCulture(string? userInterfaceCulture)
    {
        if (string.IsNullOrWhiteSpace(userInterfaceCulture)) return null;

        var trimmedUserInterfaceCulture = userInterfaceCulture.Trim();
        if (!NotificationUserInterfaceCultureConfiguration.TryCreateCultureInfo(trimmedUserInterfaceCulture, out var cultureInfo, out _)) return trimmedUserInterfaceCulture;

        return string.IsNullOrWhiteSpace(cultureInfo.Name) ? "en" : cultureInfo.Name;
    }

    private static async Task WriteJsonAsync<TValue>(
        HttpResponse response,
        TValue value,
        JsonTypeInfo<TValue> jsonTypeInfo,
        int statusCode,
        CancellationToken cancellationToken)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(response.Body, value, jsonTypeInfo, cancellationToken);
    }

    private static async Task WriteSubscriptionChangeResponseAsync(
        HttpResponse response,
        PushSubscriptionStore subscriptionStore,
        CancellationToken cancellationToken)
    {
        var activeSubscriptionCount = await subscriptionStore.CountActiveAsync(cancellationToken);
        var subscriptionChangeResponse = new PushSubscriptionChangeResponse { ActiveSubscriptionCount = activeSubscriptionCount };
        await WriteJsonAsync(
            response,
            subscriptionChangeResponse,
            LidGuardNotificationsJsonSerializerContext.Default.PushSubscriptionChangeResponse,
            StatusCodes.Status200OK,
            cancellationToken);
    }

    private static async Task WriteTextAsync(HttpResponse response, string text, int statusCode, CancellationToken cancellationToken)
    {
        response.StatusCode = statusCode;
        response.ContentType = "text/plain";
        await response.WriteAsync(text, cancellationToken);
    }
}
