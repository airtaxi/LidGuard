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
    private const int StopFollowUpConsumptionPollCycleCount = 4;
    private static readonly TimeSpan s_stopFollowUpConsumptionPollInterval = TimeSpan.FromSeconds(1);

    public static void Map(WebApplication app)
    {
        app.MapGet("/healthz", () => Results.Text("ok", "text/plain"));
        app.MapGet("/api/push/public-key", WritePublicKeyAsync);
        app.MapPost("/api/push/subscriptions", UpsertPushSubscriptionAsync).RequireAuthorization();
        app.MapDelete("/api/push/subscriptions", DeletePushSubscriptionAsync).RequireAuthorization();
        app.MapPost("/api/webhooks/lidguard/{webhookSecret}", ReceiveLidGuardWebhookAsync);
        app.MapPost("/api/follow-ups/{publicIdentifier}/reply", SubmitFollowUpReplyAsync).RequireAuthorization();
        app.MapPost("/api/follow-ups/{publicIdentifier}/extend", ExtendFollowUpAsync).RequireAuthorization();
        app.MapPost("/api/follow-ups/{publicIdentifier}/cancel", CancelFollowUpAsync).RequireAuthorization();
        app.MapGet("/api/follow-ups/{publicIdentifier}/poll/{pollToken}", PollFollowUpAsync);
    }

    private static async Task WritePublicKeyAsync(IOptions<LidGuardNotificationsOptions> options, HttpResponse response, CancellationToken cancellationToken)
    {
        var publicKeyResponse = new PublicKeyResponse { PublicKey = options.Value.VapidPublicKey };
        await WriteJsonAsync(response, publicKeyResponse, LidGuardNotificationsJsonSerializerContext.Default.PublicKeyResponse, StatusCodes.Status200OK, cancellationToken);
    }

    private static async Task UpsertPushSubscriptionAsync(HttpRequest request, HttpResponse response, PushSubscriptionStore subscriptionStore, CancellationToken cancellationToken)
    {
        var subscriptionRequest = await ReadJsonAsync(request, LidGuardNotificationsJsonSerializerContext.Default.PushSubscriptionChangeRequest, cancellationToken);
        if (!TryValidateSubscription(subscriptionRequest, out var endpoint, out var p256dhKey, out var authenticationSecret, out var errorMessage))
        {
            await WriteTextAsync(response, errorMessage, StatusCodes.Status400BadRequest, cancellationToken);
            return;
        }

        await subscriptionStore.UpsertAsync(endpoint, p256dhKey, authenticationSecret, cancellationToken);
        await WriteSubscriptionChangeResponseAsync(response, subscriptionStore, cancellationToken);
    }

    private static async Task DeletePushSubscriptionAsync(HttpRequest request, HttpResponse response, PushSubscriptionStore subscriptionStore, CancellationToken cancellationToken)
    {
        var subscriptionRequest = await ReadJsonAsync(request, LidGuardNotificationsJsonSerializerContext.Default.PushSubscriptionChangeRequest, cancellationToken);
        if (string.IsNullOrWhiteSpace(subscriptionRequest?.Endpoint))
        {
            await WriteTextAsync(response, "Endpoint is required.", StatusCodes.Status400BadRequest, cancellationToken);
            return;
        }

        await subscriptionStore.DeactivateByEndpointAsync(subscriptionRequest.Endpoint, cancellationToken);
        await WriteSubscriptionChangeResponseAsync(response, subscriptionStore, cancellationToken);
    }

    private static async Task ReceiveLidGuardWebhookAsync(string webhookSecret, HttpRequest request, HttpResponse response, IOptions<LidGuardNotificationsOptions> options, WebhookEventStore webhookEventStore, WebhookEventProcessingSignal processingSignal, CancellationToken cancellationToken)
    {
        if (!SecretVerifier.EqualsConfiguredSecret(options.Value.WebhookSecret, webhookSecret))
        {
            await WriteTextAsync(response, "Not found.", StatusCodes.Status404NotFound, cancellationToken);
            return;
        }

        var webhookRequest = await ReadJsonAsync(request, LidGuardNotificationsJsonSerializerContext.Default.LidGuardWebhookRequest, cancellationToken);
        if (!TryValidateWebhook(webhookRequest, out var eventType, out var reason, out var softLockedSessionCount, out var replyWaitSeconds, out var replyDeadlineUtc, out var errorMessage))
        {
            await WriteTextAsync(response, errorMessage, StatusCodes.Status400BadRequest, cancellationToken);
            return;
        }

        var normalizedUserInterfaceCulture = NormalizeWebhookUserInterfaceCulture(webhookRequest?.UserInterfaceCulture);
        if (eventType.Equals(LidGuardWebhookEventTypes.StopFollowUp, StringComparison.Ordinal))
        {
            var stopFollowUpRequestAcceptedResult = await webhookEventStore.InsertStopFollowUpAsync(eventType, reason, normalizedUserInterfaceCulture, softLockedSessionCount, webhookRequest?.Provider?.Trim(), webhookRequest?.ProviderName?.Trim(), webhookRequest?.SessionIdentifier?.Trim(), webhookRequest?.StartedAtUtc, webhookRequest?.LastActivityAtUtc, webhookRequest?.EndedAtUtc, webhookRequest?.EndReason?.Trim(), webhookRequest?.ActiveSessionCount, webhookRequest?.InputPromptPreview?.Trim(), webhookRequest?.LastResponse?.Trim(), replyWaitSeconds!.Value, replyDeadlineUtc!.Value, webhookRequest?.WorkingDirectory?.Trim(), webhookRequest?.TranscriptPath?.Trim(), cancellationToken);
            processingSignal.Signal();
            var pollPath = $"/api/follow-ups/{stopFollowUpRequestAcceptedResult.PublicIdentifier}/poll/{stopFollowUpRequestAcceptedResult.PollToken}";
            var acceptedResponse = new StopFollowUpWebhookAcceptedResponse
            {
                FollowUpRequestIdentifier = stopFollowUpRequestAcceptedResult.PublicIdentifier,
                ReplyPollUrl = pollPath,
                ExpiresAtUtc = stopFollowUpRequestAcceptedResult.ExpiresAtUtc
            };
            await WriteJsonAsync(response, acceptedResponse, LidGuardNotificationsJsonSerializerContext.Default.StopFollowUpWebhookAcceptedResponse, StatusCodes.Status202Accepted, cancellationToken);
            return;
        }

        await webhookEventStore.InsertAsync(eventType, reason, normalizedUserInterfaceCulture, softLockedSessionCount, webhookRequest?.Provider?.Trim(), webhookRequest?.ProviderName?.Trim(), webhookRequest?.SessionIdentifier?.Trim(), webhookRequest?.StartedAtUtc, webhookRequest?.LastActivityAtUtc, webhookRequest?.EndedAtUtc, webhookRequest?.EndReason?.Trim(), webhookRequest?.ActiveSessionCount, webhookRequest?.InputPromptPreview?.Trim(), webhookRequest?.LastResponse?.Trim(), replyWaitSeconds, replyDeadlineUtc, webhookRequest?.WorkingDirectory?.Trim(), webhookRequest?.TranscriptPath?.Trim(), cancellationToken);
        processingSignal.Signal();
        response.StatusCode = StatusCodes.Status202Accepted;
    }

    private static async Task SubmitFollowUpReplyAsync(string publicIdentifier, HttpRequest request, HttpResponse response, WebhookEventStore webhookEventStore, CancellationToken cancellationToken)
    {
        var replyRequest = await ReadJsonAsync(request, LidGuardNotificationsJsonSerializerContext.Default.StopFollowUpReplyRequest, cancellationToken);
        var reply = replyRequest?.Reply?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(reply))
        {
            var actionResult = new StopFollowUpActionResult(false, false, StopFollowUpRequestStatuses.Pending, LidGuardNotificationText.StopFollowUpReplyRequiredMessage, null, null, null);
            await WriteJsonAsync(response, CreateActionResponse(actionResult), LidGuardNotificationsJsonSerializerContext.Default.StopFollowUpActionResponse, StatusCodes.Status400BadRequest, cancellationToken);
            return;
        }

        var submissionResult = await webhookEventStore.SubmitStopFollowUpReplyAsync(publicIdentifier, reply, cancellationToken);
        if (!submissionResult.Succeeded)
        {
            var state = await webhookEventStore.GetStopFollowUpActionStateAsync(publicIdentifier, submissionResult.Message, cancellationToken);
            var actionResult = new StopFollowUpActionResult(false, false, submissionResult.Status, submissionResult.Message, state.DeadlineAtUtc, state.MaximumDeadlineAtUtc, state.ConsumedAtUtc);
            await WriteJsonAsync(response, CreateActionResponse(actionResult), LidGuardNotificationsJsonSerializerContext.Default.StopFollowUpActionResponse, GetActionFailureStatusCode(submissionResult.Status), cancellationToken);
            return;
        }

        var wasConsumed = replyRequest?.WaitForConsumption == true
            && await webhookEventStore.WaitForStopFollowUpConsumptionAsync(publicIdentifier, StopFollowUpConsumptionPollCycleCount, s_stopFollowUpConsumptionPollInterval, cancellationToken);
        var successMessage = wasConsumed ? LidGuardNotificationText.StopFollowUpReplyConsumedMessage : LidGuardNotificationText.StopFollowUpReplyAwaitingConsumptionMessage;
        var successState = await webhookEventStore.GetStopFollowUpActionStateAsync(publicIdentifier, successMessage, cancellationToken);
        successState = successState with { Message = successMessage };
        await WriteJsonAsync(response, CreateActionResponse(successState, wasConsumed || successState.ConsumedAtUtc is not null), LidGuardNotificationsJsonSerializerContext.Default.StopFollowUpActionResponse, StatusCodes.Status200OK, cancellationToken);
    }

    private static async Task ExtendFollowUpAsync(string publicIdentifier, HttpRequest request, HttpResponse response, WebhookEventStore webhookEventStore, CancellationToken cancellationToken)
    {
        var extendRequest = await ReadJsonAsync(request, LidGuardNotificationsJsonSerializerContext.Default.StopFollowUpExtendRequest, cancellationToken);
        var extendMinutes = extendRequest?.ExtendMinutes ?? StopFollowUpTiming.DefaultExtensionMinutes;
        if (extendMinutes is < StopFollowUpTiming.MinimumExtensionMinutes or > StopFollowUpTiming.MaximumExtensionMinutes)
        {
            var actionResult = new StopFollowUpActionResult(false, false, StopFollowUpRequestStatuses.Pending, LidGuardNotificationText.StopFollowUpExtendValidationMessage(StopFollowUpTiming.MinimumExtensionMinutes, StopFollowUpTiming.MaximumExtensionMinutes), null, null, null);
            await WriteJsonAsync(response, CreateActionResponse(actionResult), LidGuardNotificationsJsonSerializerContext.Default.StopFollowUpActionResponse, StatusCodes.Status400BadRequest, cancellationToken);
            return;
        }

        var extensionResult = await webhookEventStore.ExtendStopFollowUpAsync(publicIdentifier, extendMinutes, cancellationToken);
        var message = extensionResult.Message;
        if (extensionResult.Succeeded && extensionResult.Extended) message = LidGuardNotificationText.StopFollowUpExtendSucceededMessage;
        else if (extensionResult.Succeeded) message = LidGuardNotificationText.StopFollowUpExtendLimitReachedMessage;
        extensionResult = extensionResult with { Message = message };
        var statusCode = extensionResult.Succeeded ? StatusCodes.Status200OK : GetActionFailureStatusCode(extensionResult.Status);
        await WriteJsonAsync(response, CreateActionResponse(extensionResult), LidGuardNotificationsJsonSerializerContext.Default.StopFollowUpActionResponse, statusCode, cancellationToken);
    }

    private static async Task CancelFollowUpAsync(string publicIdentifier, HttpResponse response, WebhookEventStore webhookEventStore, CancellationToken cancellationToken)
    {
        var cancellationResult = await webhookEventStore.CancelStopFollowUpAsync(publicIdentifier, cancellationToken);
        var state = await webhookEventStore.GetStopFollowUpActionStateAsync(publicIdentifier, cancellationResult.Succeeded ? LidGuardNotificationText.StopFollowUpCancelSucceededMessage : cancellationResult.Message, cancellationToken);
        var actionResult = new StopFollowUpActionResult(cancellationResult.Succeeded, false, string.IsNullOrWhiteSpace(cancellationResult.Status) ? state.Status : cancellationResult.Status, cancellationResult.Succeeded ? LidGuardNotificationText.StopFollowUpCancelSucceededMessage : cancellationResult.Message, state.DeadlineAtUtc, state.MaximumDeadlineAtUtc, state.ConsumedAtUtc);
        var statusCode = cancellationResult.Succeeded ? StatusCodes.Status200OK : GetActionFailureStatusCode(cancellationResult.Status);
        await WriteJsonAsync(response, CreateActionResponse(actionResult), LidGuardNotificationsJsonSerializerContext.Default.StopFollowUpActionResponse, statusCode, cancellationToken);
    }

    private static async Task PollFollowUpAsync(string publicIdentifier, string pollToken, HttpResponse response, WebhookEventStore webhookEventStore, CancellationToken cancellationToken)
    {
        var pollResponse = await webhookEventStore.GetStopFollowUpPollResponseAsync(publicIdentifier, pollToken, cancellationToken);
        if (pollResponse is null)
        {
            await WriteTextAsync(response, "Not found.", StatusCodes.Status404NotFound, cancellationToken);
            return;
        }

        await WriteJsonAsync(response, pollResponse, LidGuardNotificationsJsonSerializerContext.Default.StopFollowUpPollResponse, StatusCodes.Status200OK, cancellationToken);
    }

    private static async Task<TValue?> ReadJsonAsync<TValue>(HttpRequest request, JsonTypeInfo<TValue> jsonTypeInfo, CancellationToken cancellationToken)
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

    private static bool TryValidateSubscription(PushSubscriptionChangeRequest? request, out string endpoint, out string p256dhKey, out string authenticationSecret, out string errorMessage)
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

    private static bool TryValidateWebhook(LidGuardWebhookRequest? request, out string eventType, out string reason, out int? softLockedSessionCount, out int? replyWaitSeconds, out DateTimeOffset? replyDeadlineUtc, out string errorMessage)
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

    private static async Task WriteJsonAsync<TValue>(HttpResponse response, TValue value, JsonTypeInfo<TValue> jsonTypeInfo, int statusCode, CancellationToken cancellationToken)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(response.Body, value, jsonTypeInfo, cancellationToken);
    }

    private static StopFollowUpActionResponse CreateActionResponse(StopFollowUpActionResult actionResult, bool replyConsumed = false)
    {
        var providerHookTimeoutRemainingSeconds = GetProviderHookTimeoutRemainingSeconds(actionResult.MaximumDeadlineAtUtc);
        return new StopFollowUpActionResponse
        {
            Succeeded = actionResult.Succeeded,
            Extended = actionResult.Extended,
            Status = actionResult.Status,
            Message = actionResult.Message,
            DeadlineAtUtc = actionResult.DeadlineAtUtc,
            MaximumDeadlineAtUtc = actionResult.MaximumDeadlineAtUtc,
            ProviderHookTimeoutRemainingSeconds = providerHookTimeoutRemainingSeconds,
            ProviderHookTimeoutRemainingText = LidGuardNotificationText.StopFollowUpProviderHookTimeoutRemaining(providerHookTimeoutRemainingSeconds),
            ReplyConsumed = replyConsumed || actionResult.ConsumedAtUtc is not null,
            ConsumedAtUtc = actionResult.ConsumedAtUtc
        };
    }

    private static int GetProviderHookTimeoutRemainingSeconds(DateTimeOffset? maximumDeadlineAtUtc)
    {
        if (maximumDeadlineAtUtc is null) return 0;

        var remainingSeconds = (maximumDeadlineAtUtc.Value - DateTimeOffset.UtcNow).TotalSeconds;
        if (remainingSeconds <= 0) return 0;
        return (int)Math.Ceiling(remainingSeconds);
    }

    private static int GetActionFailureStatusCode(string status)
        => status switch
        {
            StopFollowUpRequestStatuses.Answered => StatusCodes.Status409Conflict,
            StopFollowUpRequestStatuses.Expired => StatusCodes.Status410Gone,
            StopFollowUpRequestStatuses.Canceled => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status404NotFound
        };

    private static async Task WriteSubscriptionChangeResponseAsync(HttpResponse response, PushSubscriptionStore subscriptionStore, CancellationToken cancellationToken)
    {
        var activeSubscriptionCount = await subscriptionStore.CountActiveAsync(cancellationToken);
        var subscriptionChangeResponse = new PushSubscriptionChangeResponse { ActiveSubscriptionCount = activeSubscriptionCount };
        await WriteJsonAsync(response, subscriptionChangeResponse, LidGuardNotificationsJsonSerializerContext.Default.PushSubscriptionChangeResponse, StatusCodes.Status200OK, cancellationToken);
    }

    private static async Task WriteTextAsync(HttpResponse response, string text, int statusCode, CancellationToken cancellationToken)
    {
        response.StatusCode = statusCode;
        response.ContentType = "text/plain";
        await response.WriteAsync(text, cancellationToken);
    }
}
