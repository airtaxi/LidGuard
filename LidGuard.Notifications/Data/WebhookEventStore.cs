using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LidGuard.Notifications.Models;
using Microsoft.Data.Sqlite;

namespace LidGuard.Notifications.Data;

internal sealed class WebhookEventStore(SqliteConnectionFactory connectionFactory)
{
    public async Task<long> InsertAsync(string eventType, string reason, string? userInterfaceCulture, int? softLockedSessionCount, string? provider, string? providerName, string? sessionIdentifier, DateTimeOffset? startedAtUtc, DateTimeOffset? lastActivityAtUtc, DateTimeOffset? endedAtUtc, string? endReason, int? activeSessionCount, string? inputPromptPreview, string? lastResponse, int? replyWaitSeconds, DateTimeOffset? replyDeadlineUtc, string? workingDirectory, string? transcriptPath, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO WebhookEvents (
                EventType,
                Reason,
                UserInterfaceCulture,
                SoftLockedSessionCount,
                Provider,
                ProviderName,
                SessionIdentifier,
                StartedAtUtc,
                LastActivityAtUtc,
                EndedAtUtc,
                EndReason,
                ActiveSessionCount,
                InputPromptPreview,
                LastResponse,
                ReplyWaitSeconds,
                ReplyDeadlineUtc,
                WorkingDirectory,
                TranscriptPath,
                ReceivedAtUtc,
                Status
            )
            VALUES (
                $eventType,
                $reason,
                $userInterfaceCulture,
                $softLockedSessionCount,
                $provider,
                $providerName,
                $sessionIdentifier,
                $startedAtUtc,
                $lastActivityAtUtc,
                $endedAtUtc,
                $endReason,
                $activeSessionCount,
                $inputPromptPreview,
                $lastResponse,
                $replyWaitSeconds,
                $replyDeadlineUtc,
                $workingDirectory,
                $transcriptPath,
                $receivedAtUtc,
                $status
            )
            RETURNING Id;
            """;
        command.Parameters.AddWithValue("$eventType", eventType);
        command.Parameters.AddWithValue("$reason", reason);
        command.Parameters.AddWithValue("$userInterfaceCulture", ToDatabaseValue(userInterfaceCulture));
        command.Parameters.AddWithValue("$softLockedSessionCount", ToDatabaseValue(softLockedSessionCount));
        command.Parameters.AddWithValue("$provider", ToDatabaseValue(provider));
        command.Parameters.AddWithValue("$providerName", ToDatabaseValue(providerName));
        command.Parameters.AddWithValue("$sessionIdentifier", ToDatabaseValue(sessionIdentifier));
        command.Parameters.AddWithValue("$startedAtUtc", ToDatabaseValue(startedAtUtc));
        command.Parameters.AddWithValue("$lastActivityAtUtc", ToDatabaseValue(lastActivityAtUtc));
        command.Parameters.AddWithValue("$endedAtUtc", ToDatabaseValue(endedAtUtc));
        command.Parameters.AddWithValue("$endReason", ToDatabaseValue(endReason));
        command.Parameters.AddWithValue("$activeSessionCount", ToDatabaseValue(activeSessionCount));
        command.Parameters.AddWithValue("$inputPromptPreview", ToDatabaseValue(inputPromptPreview));
        command.Parameters.AddWithValue("$lastResponse", ToDatabaseValue(lastResponse));
        command.Parameters.AddWithValue("$replyWaitSeconds", ToDatabaseValue(replyWaitSeconds));
        command.Parameters.AddWithValue("$replyDeadlineUtc", ToDatabaseValue(replyDeadlineUtc));
        command.Parameters.AddWithValue("$workingDirectory", ToDatabaseValue(workingDirectory));
        command.Parameters.AddWithValue("$transcriptPath", ToDatabaseValue(transcriptPath));
        command.Parameters.AddWithValue("$receivedAtUtc", now);
        command.Parameters.AddWithValue("$status", WebhookEventStatuses.Pending);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    public async Task<StopFollowUpRequestAcceptedResult> InsertStopFollowUpAsync(string eventType, string reason, string? userInterfaceCulture, int? softLockedSessionCount, string? provider, string? providerName, string? sessionIdentifier, DateTimeOffset? startedAtUtc, DateTimeOffset? lastActivityAtUtc, DateTimeOffset? endedAtUtc, string? endReason, int? activeSessionCount, string? inputPromptPreview, string? lastResponse, int replyWaitSeconds, DateTimeOffset replyDeadlineUtc, string? workingDirectory, string? transcriptPath, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var publicIdentifier = Guid.NewGuid().ToString("N");
        var pollToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var pollTokenHash = ComputeHash(pollToken);

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        var webhookEventIdentifier = await InsertWebhookEventAsync(connection, transaction, eventType, reason, userInterfaceCulture, softLockedSessionCount, provider, providerName, sessionIdentifier, startedAtUtc, lastActivityAtUtc, endedAtUtc, endReason, activeSessionCount, inputPromptPreview, lastResponse, replyWaitSeconds, replyDeadlineUtc, workingDirectory, transcriptPath, now, cancellationToken);
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO StopFollowUpRequests (
                    WebhookEventId,
                    PublicIdentifier,
                    PollTokenHash,
                    Status,
                    ReplyText,
                    DeadlineAtUtc,
                    CreatedAtUtc,
                    RepliedAtUtc,
                    ConsumedAtUtc
                )
                VALUES (
                    $webhookEventId,
                    $publicIdentifier,
                    $pollTokenHash,
                    $status,
                    NULL,
                    $deadlineAtUtc,
                    $createdAtUtc,
                    NULL,
                    NULL
                );
                """;
            command.Parameters.AddWithValue("$webhookEventId", webhookEventIdentifier);
            command.Parameters.AddWithValue("$publicIdentifier", publicIdentifier);
            command.Parameters.AddWithValue("$pollTokenHash", pollTokenHash);
            command.Parameters.AddWithValue("$status", StopFollowUpRequestStatuses.Pending);
            command.Parameters.AddWithValue("$deadlineAtUtc", replyDeadlineUtc.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$createdAtUtc", now.ToString("O", CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new StopFollowUpRequestAcceptedResult(publicIdentifier, pollToken, replyDeadlineUtc);
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
                    events.Id,
                    events.EventType,
                    events.Reason,
                    events.UserInterfaceCulture,
                    events.SoftLockedSessionCount,
                    events.Provider,
                    events.ProviderName,
                    events.SessionIdentifier,
                    events.StartedAtUtc,
                    events.LastActivityAtUtc,
                    events.EndedAtUtc,
                    events.EndReason,
                    events.ActiveSessionCount,
                    events.InputPromptPreview,
                    events.LastResponse,
                    events.ReplyWaitSeconds,
                    events.ReplyDeadlineUtc,
                    events.WorkingDirectory,
                    events.TranscriptPath,
                    followUps.PublicIdentifier,
                    followUps.Status,
                    followUps.ReplyText,
                    events.ReceivedAtUtc,
                    events.AttemptCount
                FROM WebhookEvents events
                LEFT JOIN StopFollowUpRequests followUps ON followUps.WebhookEventId = events.Id
                WHERE events.Status = $pendingStatus
                    OR (events.Status = $processingStatus AND events.ReceivedAtUtc <= $staleProcessingThreshold)
                ORDER BY events.Id
                LIMIT $limit;
                """;
            selectCommand.Parameters.AddWithValue("$pendingStatus", WebhookEventStatuses.Pending);
            selectCommand.Parameters.AddWithValue("$processingStatus", WebhookEventStatuses.Processing);
            selectCommand.Parameters.AddWithValue("$staleProcessingThreshold", staleProcessingThreshold);
            selectCommand.Parameters.AddWithValue("$limit", limit);
            using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                events.Add(new PendingWebhookEvent(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetInt32(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), reader.IsDBNull(9) ? null : DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), reader.IsDBNull(10) ? null : DateTimeOffset.Parse(reader.GetString(10), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), reader.IsDBNull(11) ? null : reader.GetString(11), reader.IsDBNull(12) ? null : reader.GetInt32(12), reader.IsDBNull(13) ? null : reader.GetString(13), reader.IsDBNull(14) ? null : reader.GetString(14), reader.IsDBNull(15) ? null : reader.GetInt32(15), reader.IsDBNull(16) ? null : DateTimeOffset.Parse(reader.GetString(16), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), reader.IsDBNull(17) ? null : reader.GetString(17), reader.IsDBNull(18) ? null : reader.GetString(18), reader.IsDBNull(19) ? null : reader.GetString(19), reader.IsDBNull(20) ? null : reader.GetString(20), reader.IsDBNull(21) ? null : reader.GetString(21), DateTimeOffset.Parse(reader.GetString(22), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), Convert.ToInt32(reader.GetValue(23), CultureInfo.InvariantCulture) + 1));
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

    public async Task<StopFollowUpReplySubmissionResult> SubmitStopFollowUpReplyAsync(string publicIdentifier, string reply, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        await ExpirePendingStopFollowUpRequestsAsync(connection, transaction, cancellationToken);

        using var selectCommand = connection.CreateCommand();
        selectCommand.Transaction = transaction;
        selectCommand.CommandText =
            """
            SELECT Status
            FROM StopFollowUpRequests
            WHERE PublicIdentifier = $publicIdentifier;
            """;
        selectCommand.Parameters.AddWithValue("$publicIdentifier", publicIdentifier);
        var statusObject = await selectCommand.ExecuteScalarAsync(cancellationToken);
        if (statusObject is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new StopFollowUpReplySubmissionResult(false, string.Empty, "The follow-up request was not found.");
        }

        var status = Convert.ToString(statusObject, CultureInfo.InvariantCulture) ?? string.Empty;
        if (status.Equals(StopFollowUpRequestStatuses.Answered, StringComparison.Ordinal))
        {
            await transaction.CommitAsync(cancellationToken);
            return new StopFollowUpReplySubmissionResult(false, status, "A reply was already submitted for this follow-up request.");
        }
        if (status.Equals(StopFollowUpRequestStatuses.Expired, StringComparison.Ordinal))
        {
            await transaction.CommitAsync(cancellationToken);
            return new StopFollowUpReplySubmissionResult(false, status, "This follow-up request has already expired.");
        }
        if (status.Equals(StopFollowUpRequestStatuses.Canceled, StringComparison.Ordinal))
        {
            await transaction.CommitAsync(cancellationToken);
            return new StopFollowUpReplySubmissionResult(false, status, "This follow-up request was canceled.");
        }

        using var updateCommand = connection.CreateCommand();
        updateCommand.Transaction = transaction;
        updateCommand.CommandText =
            """
            UPDATE StopFollowUpRequests
            SET Status = $status,
                ReplyText = $replyText,
                RepliedAtUtc = $repliedAtUtc
            WHERE PublicIdentifier = $publicIdentifier;
            """;
        updateCommand.Parameters.AddWithValue("$status", StopFollowUpRequestStatuses.Answered);
        updateCommand.Parameters.AddWithValue("$replyText", reply);
        updateCommand.Parameters.AddWithValue("$repliedAtUtc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        updateCommand.Parameters.AddWithValue("$publicIdentifier", publicIdentifier);
        await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new StopFollowUpReplySubmissionResult(true, StopFollowUpRequestStatuses.Answered, "Reply submitted.");
    }

    public async Task<StopFollowUpCancellationResult> CancelStopFollowUpAsync(string publicIdentifier, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        await ExpirePendingStopFollowUpRequestsAsync(connection, transaction, cancellationToken);

        using var selectCommand = connection.CreateCommand();
        selectCommand.Transaction = transaction;
        selectCommand.CommandText =
            """
            SELECT Status
            FROM StopFollowUpRequests
            WHERE PublicIdentifier = $publicIdentifier;
            """;
        selectCommand.Parameters.AddWithValue("$publicIdentifier", publicIdentifier);
        var statusObject = await selectCommand.ExecuteScalarAsync(cancellationToken);
        if (statusObject is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new StopFollowUpCancellationResult(false, string.Empty, "The follow-up request was not found.");
        }

        var status = Convert.ToString(statusObject, CultureInfo.InvariantCulture) ?? string.Empty;
        if (status.Equals(StopFollowUpRequestStatuses.Answered, StringComparison.Ordinal))
        {
            await transaction.CommitAsync(cancellationToken);
            return new StopFollowUpCancellationResult(false, status, "A reply was already submitted for this follow-up request.");
        }

        if (status.Equals(StopFollowUpRequestStatuses.Expired, StringComparison.Ordinal))
        {
            await transaction.CommitAsync(cancellationToken);
            return new StopFollowUpCancellationResult(false, status, "This follow-up request has already expired.");
        }

        if (status.Equals(StopFollowUpRequestStatuses.Canceled, StringComparison.Ordinal))
        {
            await transaction.CommitAsync(cancellationToken);
            return new StopFollowUpCancellationResult(true, status, "The follow-up request was already canceled.");
        }

        using var updateCommand = connection.CreateCommand();
        updateCommand.Transaction = transaction;
        updateCommand.CommandText =
            """
            UPDATE StopFollowUpRequests
            SET Status = $status,
                ReplyText = NULL,
                RepliedAtUtc = NULL
            WHERE PublicIdentifier = $publicIdentifier;
            """;
        updateCommand.Parameters.AddWithValue("$status", StopFollowUpRequestStatuses.Canceled);
        updateCommand.Parameters.AddWithValue("$publicIdentifier", publicIdentifier);
        await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new StopFollowUpCancellationResult(true, StopFollowUpRequestStatuses.Canceled, "Follow-up request canceled.");
    }

    public async Task<StopFollowUpPollResponse?> GetStopFollowUpPollResponseAsync(string publicIdentifier, string pollToken, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        await ExpirePendingStopFollowUpRequestsAsync(connection, transaction, cancellationToken);

        using var selectCommand = connection.CreateCommand();
        selectCommand.Transaction = transaction;
        selectCommand.CommandText =
            """
            SELECT PollTokenHash, Status, ReplyText, ConsumedAtUtc
            FROM StopFollowUpRequests
            WHERE PublicIdentifier = $publicIdentifier;
            """;
        selectCommand.Parameters.AddWithValue("$publicIdentifier", publicIdentifier);
        using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        var pollTokenHash = reader.GetString(0);
        if (!pollTokenHash.Equals(ComputeHash(pollToken), StringComparison.Ordinal)) return null;

        var status = reader.GetString(1);
        var replyText = reader.IsDBNull(2) ? null : reader.GetString(2);
        var consumedAtUtc = reader.IsDBNull(3) ? null : reader.GetString(3);
        if (status.Equals(StopFollowUpRequestStatuses.Answered, StringComparison.Ordinal) && string.IsNullOrWhiteSpace(consumedAtUtc))
        {
            using var updateCommand = connection.CreateCommand();
            updateCommand.Transaction = transaction;
            updateCommand.CommandText =
                """
                UPDATE StopFollowUpRequests
                SET ConsumedAtUtc = $consumedAtUtc
                WHERE PublicIdentifier = $publicIdentifier;
                """;
            updateCommand.Parameters.AddWithValue("$consumedAtUtc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            updateCommand.Parameters.AddWithValue("$publicIdentifier", publicIdentifier);
            await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
        return new StopFollowUpPollResponse
        {
            Status = status,
            Reply = status.Equals(StopFollowUpRequestStatuses.Answered, StringComparison.Ordinal) ? replyText : null
        };
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
                events.UserInterfaceCulture,
                events.SoftLockedSessionCount,
                events.Provider,
                events.ProviderName,
                events.SessionIdentifier,
                events.StartedAtUtc,
                events.LastActivityAtUtc,
                events.EndedAtUtc,
                events.EndReason,
                events.ActiveSessionCount,
                events.InputPromptPreview,
                events.LastResponse,
                events.ReplyWaitSeconds,
                events.ReplyDeadlineUtc,
                events.WorkingDirectory,
                events.TranscriptPath,
                followUps.PublicIdentifier,
                followUps.Status,
                followUps.ReplyText,
                followUps.RepliedAtUtc,
                followUps.ConsumedAtUtc,
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
            LEFT JOIN StopFollowUpRequests followUps ON followUps.WebhookEventId = events.Id
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
            events.Add(new WebhookEventSummary(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetInt32(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), reader.IsDBNull(9) ? null : DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), reader.IsDBNull(10) ? null : DateTimeOffset.Parse(reader.GetString(10), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), reader.IsDBNull(11) ? null : reader.GetString(11), reader.IsDBNull(12) ? null : reader.GetInt32(12), reader.IsDBNull(13) ? null : reader.GetString(13), reader.IsDBNull(14) ? null : reader.GetString(14), reader.IsDBNull(15) ? null : reader.GetInt32(15), reader.IsDBNull(16) ? null : DateTimeOffset.Parse(reader.GetString(16), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), reader.IsDBNull(17) ? null : reader.GetString(17), reader.IsDBNull(18) ? null : reader.GetString(18), reader.IsDBNull(19) ? null : reader.GetString(19), reader.IsDBNull(20) ? null : reader.GetString(20), reader.IsDBNull(21) ? null : reader.GetString(21), reader.IsDBNull(22) ? null : DateTimeOffset.Parse(reader.GetString(22), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), reader.IsDBNull(23) ? null : DateTimeOffset.Parse(reader.GetString(23), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), DateTimeOffset.Parse(reader.GetString(24), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), reader.IsDBNull(25) ? null : DateTimeOffset.Parse(reader.GetString(25), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), reader.GetString(26), Convert.ToInt32(reader.GetValue(27), CultureInfo.InvariantCulture), Convert.ToInt32(reader.GetValue(28), CultureInfo.InvariantCulture), Convert.ToInt32(reader.GetValue(29), CultureInfo.InvariantCulture), Convert.ToInt32(reader.GetValue(30), CultureInfo.InvariantCulture), Convert.ToInt32(reader.GetValue(31), CultureInfo.InvariantCulture), reader.IsDBNull(32) ? null : reader.GetString(32)));
        }

        return events;
    }

    private static async Task ExpirePendingStopFollowUpRequestsAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE StopFollowUpRequests
            SET Status = $expiredStatus
            WHERE Status = $pendingStatus
                AND DeadlineAtUtc <= $nowUtc;
            """;
        command.Parameters.AddWithValue("$expiredStatus", StopFollowUpRequestStatuses.Expired);
        command.Parameters.AddWithValue("$pendingStatus", StopFollowUpRequestStatuses.Pending);
        command.Parameters.AddWithValue("$nowUtc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string ComputeHash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static object ToDatabaseValue(int? value) => value.HasValue ? value.Value : DBNull.Value;

    private static object ToDatabaseValue(DateTimeOffset? value)
        => value.HasValue ? value.Value.ToString("O", CultureInfo.InvariantCulture) : DBNull.Value;

    private static object ToDatabaseValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static async Task<long> InsertWebhookEventAsync(SqliteConnection connection, SqliteTransaction transaction, string eventType, string reason, string? userInterfaceCulture, int? softLockedSessionCount, string? provider, string? providerName, string? sessionIdentifier, DateTimeOffset? startedAtUtc, DateTimeOffset? lastActivityAtUtc, DateTimeOffset? endedAtUtc, string? endReason, int? activeSessionCount, string? inputPromptPreview, string? lastResponse, int? replyWaitSeconds, DateTimeOffset? replyDeadlineUtc, string? workingDirectory, string? transcriptPath, DateTimeOffset receivedAtUtc, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO WebhookEvents (
                EventType,
                Reason,
                UserInterfaceCulture,
                SoftLockedSessionCount,
                Provider,
                ProviderName,
                SessionIdentifier,
                StartedAtUtc,
                LastActivityAtUtc,
                EndedAtUtc,
                EndReason,
                ActiveSessionCount,
                InputPromptPreview,
                LastResponse,
                ReplyWaitSeconds,
                ReplyDeadlineUtc,
                WorkingDirectory,
                TranscriptPath,
                ReceivedAtUtc,
                Status
            )
            VALUES (
                $eventType,
                $reason,
                $userInterfaceCulture,
                $softLockedSessionCount,
                $provider,
                $providerName,
                $sessionIdentifier,
                $startedAtUtc,
                $lastActivityAtUtc,
                $endedAtUtc,
                $endReason,
                $activeSessionCount,
                $inputPromptPreview,
                $lastResponse,
                $replyWaitSeconds,
                $replyDeadlineUtc,
                $workingDirectory,
                $transcriptPath,
                $receivedAtUtc,
                $status
            )
            RETURNING Id;
            """;
        command.Parameters.AddWithValue("$eventType", eventType);
        command.Parameters.AddWithValue("$reason", reason);
        command.Parameters.AddWithValue("$userInterfaceCulture", ToDatabaseValue(userInterfaceCulture));
        command.Parameters.AddWithValue("$softLockedSessionCount", ToDatabaseValue(softLockedSessionCount));
        command.Parameters.AddWithValue("$provider", ToDatabaseValue(provider));
        command.Parameters.AddWithValue("$providerName", ToDatabaseValue(providerName));
        command.Parameters.AddWithValue("$sessionIdentifier", ToDatabaseValue(sessionIdentifier));
        command.Parameters.AddWithValue("$startedAtUtc", ToDatabaseValue(startedAtUtc));
        command.Parameters.AddWithValue("$lastActivityAtUtc", ToDatabaseValue(lastActivityAtUtc));
        command.Parameters.AddWithValue("$endedAtUtc", ToDatabaseValue(endedAtUtc));
        command.Parameters.AddWithValue("$endReason", ToDatabaseValue(endReason));
        command.Parameters.AddWithValue("$activeSessionCount", ToDatabaseValue(activeSessionCount));
        command.Parameters.AddWithValue("$inputPromptPreview", ToDatabaseValue(inputPromptPreview));
        command.Parameters.AddWithValue("$lastResponse", ToDatabaseValue(lastResponse));
        command.Parameters.AddWithValue("$replyWaitSeconds", ToDatabaseValue(replyWaitSeconds));
        command.Parameters.AddWithValue("$replyDeadlineUtc", ToDatabaseValue(replyDeadlineUtc));
        command.Parameters.AddWithValue("$workingDirectory", ToDatabaseValue(workingDirectory));
        command.Parameters.AddWithValue("$transcriptPath", ToDatabaseValue(transcriptPath));
        command.Parameters.AddWithValue("$receivedAtUtc", receivedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$status", WebhookEventStatuses.Pending);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }
}
