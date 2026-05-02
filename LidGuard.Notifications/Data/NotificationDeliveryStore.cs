using System.Globalization;

namespace LidGuard.Notifications.Data;

internal sealed class NotificationDeliveryStore(SqliteConnectionFactory connectionFactory)
{
    public async Task InsertAsync(
        long webhookEventIdentifier,
        long subscriptionIdentifier,
        string status,
        int? httpStatusCode,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO NotificationDeliveries (
                WebhookEventId,
                SubscriptionId,
                Status,
                HttpStatusCode,
                Error,
                CreatedAtUtc
            )
            VALUES ($webhookEventIdentifier, $subscriptionIdentifier, $status, $httpStatusCode, $errorMessage, $createdAtUtc);
            """;
        command.Parameters.AddWithValue("$webhookEventIdentifier", webhookEventIdentifier);
        command.Parameters.AddWithValue("$subscriptionIdentifier", subscriptionIdentifier);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$httpStatusCode", ToDatabaseValue(httpStatusCode));
        command.Parameters.AddWithValue("$errorMessage", string.IsNullOrWhiteSpace(errorMessage) ? DBNull.Value : errorMessage);
        command.Parameters.AddWithValue("$createdAtUtc", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static object ToDatabaseValue(int? value) => value.HasValue ? value.Value : DBNull.Value;
}
