using System.Text;
using System.Text.Json;
using LidGuard.Settings;
using LidGuardLib.Commons.Results;

namespace LidGuard.Runtime;

internal static class SuspendWebhookSender
{
    private static readonly HttpClient s_httpClient = new();

    public static async Task<LidGuardOperationResult> SendAsync(
        string preSuspendWebhookUrl,
        SuspendWebhookReason reason,
        int softLockedSessionCount,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        if (!PreSuspendWebhookConfiguration.TryNormalizeConfiguredValue(preSuspendWebhookUrl, out var normalizedPreSuspendWebhookUrl, out var message)) return LidGuardOperationResult.Failure(message);

        if (string.IsNullOrWhiteSpace(normalizedPreSuspendWebhookUrl)) return LidGuardOperationResult.Success();

        var request = new LidGuardWebhookRequest
        {
            EventType = LidGuardWebhookEventTypes.PreSuspend,
            Reason = reason.ToString(),
            SoftLockedSessionCount = reason == SuspendWebhookReason.SoftLocked ? softLockedSessionCount : null
        };
        return await SendCoreAsync(
            normalizedPreSuspendWebhookUrl,
            request,
            "pre-suspend",
            cancellationToken,
            timeout);
    }

    public static async Task<LidGuardOperationResult> SendPostSessionEndAsync(
        string postSessionEndWebhookUrl,
        LidGuardWebhookRequest request,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        if (!PostSessionEndWebhookConfiguration.TryNormalizeConfiguredValue(postSessionEndWebhookUrl, out var normalizedPostSessionEndWebhookUrl, out var message)) return LidGuardOperationResult.Failure(message);

        if (string.IsNullOrWhiteSpace(normalizedPostSessionEndWebhookUrl)) return LidGuardOperationResult.Success();
        return await SendCoreAsync(
            normalizedPostSessionEndWebhookUrl,
            request,
            "post-session-end",
            cancellationToken,
            timeout);
    }

    private static async Task<LidGuardOperationResult> SendCoreAsync(
        string webhookUrl,
        LidGuardWebhookRequest request,
        string displayName,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        var requestContent = JsonSerializer.Serialize(request, SuspendWebhookJsonSerializerContext.Default.LidGuardWebhookRequest);
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, webhookUrl)
        {
            Content = new StringContent(requestContent, Encoding.UTF8, "application/json")
        };
        using var timeoutCancellationTokenSource = timeout.HasValue ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken) : null;
        var effectiveCancellationToken = cancellationToken;
        if (timeoutCancellationTokenSource is not null)
        {
            timeoutCancellationTokenSource.CancelAfter(timeout.Value);
            effectiveCancellationToken = timeoutCancellationTokenSource.Token;
        }

        try
        {
            using var response = await s_httpClient.SendAsync(requestMessage, effectiveCancellationToken);
            if (response.IsSuccessStatusCode) return LidGuardOperationResult.Success();

            return LidGuardOperationResult.Failure(
                $"The {displayName} webhook returned {(int)response.StatusCode} ({response.ReasonPhrase}).");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return LidGuardOperationResult.Failure($"The {displayName} webhook request timed out.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            return LidGuardOperationResult.Failure($"Failed to send the {displayName} webhook: {exception.Message}");
        }
    }
}
