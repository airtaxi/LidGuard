using System.Globalization;
using Microsoft.Data.Sqlite;

namespace LidGuard.Notifications.Data;

internal sealed class WebhookEventStore(SqliteConnectionFactory connectionFactory)
{
    public async Task<long> InsertAsync(
        string eventType,
        string reason,
        int? softLockedSessionCount,
        string? provider,
        string? providerName,
        string? sessionIdentifier,
        DateTimeOffset? startedAtUtc,
        DateTimeOffset? lastActivityAtUtc,
        DateTimeOffset? endedAtUtc,
        string? endReason,
        int? activeSessionCount,
        string? workingDirectory,
        string? transcriptPath,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO WebhookEvents (
                EventType,
                Reason,
                SoftLockedSessionCount,
                Provider,
                ProviderName,
                SessionIdentifier,
                StartedAtUtc,
                LastActivityAtUtc,
                EndedAtUtc,
                EndReason,
                ActiveSessionCount,
                WorkingDirectory,
                TranscriptPath,
                ReceivedAtUtc,
                Status
            )
            VALUES (
                $eventType,
                $reason,
                $softLockedSessionCount,
                $provider,
                $providerName,
                $sessionIdentifier,
                $startedAtUtc,
                $lastActivityAtUtc,
                $endedAtUtc,
                $endReason,
                $activeSessionCount,
                $workingDirectory,
                $transcriptPath,
                $receivedAtUtc,
                $status
            )
            RETURNING Id;
            """;
        command.Parameters.AddWithValue("$eventType", eventType);
        command.Parameters.AddWithValue("$reason", reason);
        command.Parameters.AddWithValue("$softLockedSessionCount", ToDatabaseValue(softLockedSessionCount));
        command.Parameters.AddWithValue("$provider", ToDatabaseValue(provider));
        command.Parameters.AddWithValue("$providerName", ToDatabaseValue(providerName));
        command.Parameters.AddWithValue("$sessionIdentifier", ToDatabaseValue(sessionIdentifier));
        command.Parameters.AddWithValue("$startedAtUtc", ToDatabaseValue(startedAtUtc));
        command.Parameters.AddWithValue("$lastActivityAtUtc", ToDatabaseValue(lastActivityAtUtc));
        command.Parameters.AddWithValue("$endedAtUtc", ToDatabaseValue(endedAtUtc));
        command.Parameters.AddWithValue("$endReason", ToDatabaseValue(endReason));
        command.Parameters.AddWithValue("$activeSessionCount", ToDatabaseValue(activeSessionCount));
        command.Parameters.AddWithValue("$workingDirectory", ToDatabaseValue(workingDirectory));
        command.Parameters.AddWithValue("$transcriptPath", ToDatabaseValue(transcriptPath));
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
                SELECT
                    Id,
                    EventType,
                    Reason,
                    SoftLockedSessionCount,
                    Provider,
                    ProviderName,
                    SessionIdentifier,
                    StartedAtUtc,
                    LastActivityAtUtc,
                    EndedAtUtc,
                    EndReason,
                    ActiveSessionCount,
                    WorkingDirectory,
                    TranscriptPath,
                    ReceivedAtUtc,
                    AttemptCount
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
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    reader.IsDBNull(9) ? null : DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.IsDBNull(11) ? null : reader.GetInt32(11),
                    reader.IsDBNull(12) ? null : reader.GetString(12),
                    reader.IsDBNull(13) ? null : reader.GetString(13),
                    DateTimeOffset.Parse(reader.GetString(14), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    Convert.ToInt32(reader.GetValue(15), CultureInfo.InvariantCulture) + 1));
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
                events.EventType,
                events.Reason,
                events.SoftLockedSessionCount,
                events.Provider,
                events.ProviderName,
                events.SessionIdentifier,
                events.StartedAtUtc,
                events.LastActivityAtUtc,
                events.EndedAtUtc,
                events.EndReason,
                events.ActiveSessionCount,
                events.WorkingDirectory,
                events.TranscriptPath,
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
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.IsDBNull(9) ? null : DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetInt32(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                DateTimeOffset.Parse(reader.GetString(14), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.IsDBNull(15) ? null : DateTimeOffset.Parse(reader.GetString(15), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.GetString(16),
                Convert.ToInt32(reader.GetValue(17), CultureInfo.InvariantCulture),
                Convert.ToInt32(reader.GetValue(18), CultureInfo.InvariantCulture),
                Convert.ToInt32(reader.GetValue(19), CultureInfo.InvariantCulture),
                Convert.ToInt32(reader.GetValue(20), CultureInfo.InvariantCulture),
                Convert.ToInt32(reader.GetValue(21), CultureInfo.InvariantCulture),
                reader.IsDBNull(22) ? null : reader.GetString(22)));
        }

        return events;
    }

    private static object ToDatabaseValue(int? value) => value.HasValue ? value.Value : DBNull.Value;

    private static object ToDatabaseValue(DateTimeOffset? value)
        => value.HasValue ? value.Value.ToString("O", CultureInfo.InvariantCulture) : DBNull.Value;

    private static object ToDatabaseValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
}
