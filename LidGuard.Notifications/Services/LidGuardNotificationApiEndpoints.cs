using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using LidGuard.Notifications.Configuration;
using LidGuard.Notifications.Data;
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
            response.StatusCode = StatusCodes.Status204NoContent;
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
            response.StatusCode = StatusCodes.Status204NoContent;
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
            if (!TryValidateWebhook(webhookRequest, out var reason, out var softLockedSessionCount, out var errorMessage))
            {
                await WriteTextAsync(response, errorMessage, StatusCodes.Status400BadRequest, cancellationToken);
                return;
            }

            await webhookEventStore.InsertAsync(reason, softLockedSessionCount, cancellationToken);
            processingSignal.Signal();
            response.StatusCode = StatusCodes.Status202Accepted;
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
        out string reason,
        out int? softLockedSessionCount,
        out string errorMessage)
    {
        reason = request?.Reason?.Trim() ?? string.Empty;
        softLockedSessionCount = request?.SoftLockedSessionCount;

        if (!LidGuardWebhookReasons.IsRecognized(reason))
        {
            errorMessage = "reason must be Completed, SoftLocked, or EmergencyHibernation.";
            return false;
        }

        if (softLockedSessionCount.HasValue && softLockedSessionCount.Value < 0)
        {
            errorMessage = "softLockedSessionCount must be zero or greater when supplied.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
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

    private static async Task WriteTextAsync(HttpResponse response, string text, int statusCode, CancellationToken cancellationToken)
    {
        response.StatusCode = statusCode;
        response.ContentType = "text/plain";
        await response.WriteAsync(text, cancellationToken);
    }
}
