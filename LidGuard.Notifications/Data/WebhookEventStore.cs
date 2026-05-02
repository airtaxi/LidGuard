using System.Globalization;
using Microsoft.Data.Sqlite;

namespace LidGuard.Notifications.Data;

internal sealed class WebhookEventStore(SqliteConnectionFactory connectionFactory)
{
    public async Task<long> InsertAsync(string reason, int? softLockedSessionCount, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO WebhookEvents (
                Reason,
                SoftLockedSessionCount,
                ReceivedAtUtc,
                Status
            )
            VALUES ($reason, $softLockedSessionCount, $receivedAtUtc, $status)
            RETURNING Id;
            """;
        command.Parameters.AddWithValue("$reason", reason);
        command.Parameters.AddWithValue("$softLockedSessionCount", ToDatabaseValue(softLockedSessionCount));
        command.Parameters.AddWithValue("$receivedAtUtc", now);
        command.Parameters.AddWithValue("$status", WebhookEventStatuses.Pending);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    public async Task<IReadOnlyList<PendingWebhookEvent>> ClaimPendingAsync(int limit, CancellationToken cancellationToken)
    {
        var events = new List<PendingWebhookEvent>();
        var staleProcessingThreshold = DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        using (var selectCommand = connection.CreateCommand())
        {
            selectCommand.Transaction = transaction;
            selectCommand.CommandText =
                """
                SELECT Id, Reason, SoftLockedSessionCount, ReceivedAtUtc, AttemptCount
                FROM WebhookEvents
                WHERE Status = $pendingStatus
                    OR (Status = $processingStatus AND ReceivedAtUtc <= $staleProcessingThreshold)
                ORDER BY Id
                LIMIT $limit;
                """;
            selectCommand.Parameters.AddWithValue("$pendingStatus", WebhookEventStatuses.Pending);
            selectCommand.Parameters.AddWithValue("$processingStatus", WebhookEventStatuses.Processing);
            selectCommand.Parameters.AddWithValue("$staleProcessingThreshold", staleProcessingThreshold);
            selectCommand.Parameters.AddWithValue("$limit", limit);
            using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                events.Add(new PendingWebhookEvent(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    Convert.ToInt32(reader.GetValue(4), CultureInfo.InvariantCulture) + 1));
            }
        }

        foreach (var webhookEvent in events)
        {
            using var updateCommand = connection.CreateCommand();
            updateCommand.Transaction = transaction;
            updateCommand.CommandText =
                """
                UPDATE WebhookEvents
                SET Status = $processingStatus,
                    AttemptCount = AttemptCount + 1,
                    LastError = NULL
                WHERE Id = $webhookEventIdentifier;
                """;
            updateCommand.Parameters.AddWithValue("$processingStatus", WebhookEventStatuses.Processing);
            updateCommand.Parameters.AddWithValue("$webhookEventIdentifier", webhookEvent.WebhookEventIdentifier);
            await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
        return events;
    }

    public async Task CompleteAsync(long webhookEventIdentifier, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE WebhookEvents
            SET Status = $completedStatus,
                ProcessedAtUtc = $processedAtUtc,
                LastError = NULL
            WHERE Id = $webhookEventIdentifier;
            """;
        command.Parameters.AddWithValue("$completedStatus", WebhookEventStatuses.Completed);
        command.Parameters.AddWithValue("$processedAtUtc", now);
        command.Parameters.AddWithValue("$webhookEventIdentifier", webhookEventIdentifier);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task FailAsync(long webhookEventIdentifier, string errorMessage, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE WebhookEvents
            SET Status = $failedStatus,
                ProcessedAtUtc = $processedAtUtc,
                LastError = $lastError
            WHERE Id = $webhookEventIdentifier;
            """;
        command.Parameters.AddWithValue("$failedStatus", WebhookEventStatuses.Failed);
        command.Parameters.AddWithValue("$processedAtUtc", now);
        command.Parameters.AddWithValue("$lastError", errorMessage);
        command.Parameters.AddWithValue("$webhookEventIdentifier", webhookEventIdentifier);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WebhookEventSummary>> ListRecentAsync(int limit, CancellationToken cancellationToken)
    {
        var events = new List<WebhookEventSummary>();
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                events.Id,
                events.Reason,
                events.SoftLockedSessionCount,
                events.ReceivedAtUtc,
                events.ProcessedAtUtc,
                events.Status,
                events.AttemptCount,
                COUNT(deliveries.Id) AS DeliveryCount,
                COALESCE(SUM(CASE WHEN deliveries.Status = $succeededStatus THEN 1 ELSE 0 END), 0) AS SuccessCount,
                COALESCE(SUM(CASE WHEN deliveries.Status = $permanentFailureStatus THEN 1 ELSE 0 END), 0) AS PermanentFailureCount,
                COALESCE(SUM(CASE WHEN deliveries.Status = $transientFailureStatus THEN 1 ELSE 0 END), 0) AS TransientFailureCount,
                events.LastError
            FROM WebhookEvents events
            LEFT JOIN NotificationDeliveries deliveries ON deliveries.WebhookEventId = events.Id
            GROUP BY events.Id
            ORDER BY events.Id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$succeededStatus", DeliveryStatuses.Succeeded);
        command.Parameters.AddWithValue("$permanentFailureStatus", DeliveryStatuses.PermanentFailure);
        command.Parameters.AddWithValue("$transientFailureStatus", DeliveryStatuses.TransientFailure);
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new WebhookEventSummary(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.GetString(5),
                Convert.ToInt32(reader.GetValue(6), CultureInfo.InvariantCulture),
                Convert.ToInt32(reader.GetValue(7), CultureInfo.InvariantCulture),
                Convert.ToInt32(reader.GetValue(8), CultureInfo.InvariantCulture),
                Convert.ToInt32(reader.GetValue(9), CultureInfo.InvariantCulture),
                Convert.ToInt32(reader.GetValue(10), CultureInfo.InvariantCulture),
                reader.IsDBNull(11) ? null : reader.GetString(11)));
        }

        return events;
    }

    private static object ToDatabaseValue(int? value) => value.HasValue ? value.Value : DBNull.Value;
}
