using System.Text;
using System.Text.Json;
using LidGuard.Results;

namespace LidGuard.Runtime;

internal static class StopFollowUpWebhookClient
{
    private static readonly HttpClient s_httpClient = new();

    public static async Task<LidGuardOperationResult<StopFollowUpWebhookStartResponse>> StartAsync(string webhookUrl, LidGuardWebhookRequest request, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        var requestContent = JsonSerializer.Serialize(request, SuspendWebhookJsonSerializerContext.Default.LidGuardWebhookRequest);
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, webhookUrl)
        {
            Content = new StringContent(requestContent, Encoding.UTF8, "application/json")
        };

        try
        {
            using var response = await SendAsync(requestMessage, cancellationToken, timeout);
            if (!response.IsSuccessStatusCode)
            {
                return LidGuardOperationResult<StopFollowUpWebhookStartResponse>.Failure($"The closed-lid stop follow-up webhook returned {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var startResponse = await JsonSerializer.DeserializeAsync(responseStream, SuspendWebhookJsonSerializerContext.Default.StopFollowUpWebhookStartResponse, cancellationToken);
            if (startResponse is null) return LidGuardOperationResult<StopFollowUpWebhookStartResponse>.Failure("The closed-lid stop follow-up webhook returned an empty response body.");

            return LidGuardOperationResult<StopFollowUpWebhookStartResponse>.Success(startResponse);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return LidGuardOperationResult<StopFollowUpWebhookStartResponse>.Failure("The closed-lid stop follow-up webhook request timed out.");
        }
        catch (JsonException exception)
        {
            return LidGuardOperationResult<StopFollowUpWebhookStartResponse>.Failure($"The closed-lid stop follow-up webhook returned invalid JSON: {exception.Message}");
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            return LidGuardOperationResult<StopFollowUpWebhookStartResponse>.Failure($"Failed to send the closed-lid stop follow-up webhook: {exception.Message}");
        }
    }

    public static async Task<LidGuardOperationResult<StopFollowUpWebhookPollResponse>> PollAsync(Uri pollUri, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        using var requestMessage = new HttpRequestMessage(HttpMethod.Get, pollUri);
        try
        {
            using var response = await SendAsync(requestMessage, cancellationToken, timeout);
            if (!response.IsSuccessStatusCode)
            {
                return LidGuardOperationResult<StopFollowUpWebhookPollResponse>.Failure($"The closed-lid stop follow-up poll returned {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var pollResponse = await JsonSerializer.DeserializeAsync(responseStream, SuspendWebhookJsonSerializerContext.Default.StopFollowUpWebhookPollResponse, cancellationToken);
            if (pollResponse is null) return LidGuardOperationResult<StopFollowUpWebhookPollResponse>.Failure("The closed-lid stop follow-up poll returned an empty response body.");

            return LidGuardOperationResult<StopFollowUpWebhookPollResponse>.Success(pollResponse);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return LidGuardOperationResult<StopFollowUpWebhookPollResponse>.Failure("The closed-lid stop follow-up poll request timed out.");
        }
        catch (JsonException exception)
        {
            return LidGuardOperationResult<StopFollowUpWebhookPollResponse>.Failure($"The closed-lid stop follow-up poll returned invalid JSON: {exception.Message}");
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            return LidGuardOperationResult<StopFollowUpWebhookPollResponse>.Failure($"Failed to poll the closed-lid stop follow-up webhook: {exception.Message}");
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpRequestMessage requestMessage, CancellationToken cancellationToken, TimeSpan? timeout)
    {
        using var timeoutCancellationTokenSource = timeout.HasValue ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken) : null;
        var effectiveCancellationToken = cancellationToken;
        if (timeoutCancellationTokenSource is not null)
        {
            timeoutCancellationTokenSource.CancelAfter(timeout.Value);
            effectiveCancellationToken = timeoutCancellationTokenSource.Token;
        }

        return await s_httpClient.SendAsync(requestMessage, effectiveCancellationToken);
    }
}
