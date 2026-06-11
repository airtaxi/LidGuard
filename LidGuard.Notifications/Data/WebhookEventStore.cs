using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LidGuard.Notifications.Models;
using Microsoft.Data.Sqlite;

namespace LidGuard.Notifications.Data;

internal sealed class WebhookEventStore(SqliteConnectionFactory connectionFactory)
{
    public async Task<long> InsertAsync(string eventType, string reason, string? userInterfaceCulture, int? softLockedSessionCount, string? provider, string? providerName, string? sessionIdentifier, DateTimeOffset? startedAtUtc, DateTimeOffset? lastActivityAtUtc, DateTimeOffset? endedAtUtc, string? endReason, int? activeSessionCount, string? inputPromptPreview, string? lastAssistantMessage, int? replyWaitSeconds, DateTimeOffset? replyDeadlineUtc, string? workingDirectory, string? transcriptPath, CancellationToken cancellationToken)
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
        command.Parameters.AddWithValue("$lastResponse", ToDatabaseValue(lastAssistantMessage));
        command.Parameters.AddWithValue("$replyWaitSeconds", ToDatabaseValue(replyWaitSeconds));
        command.Parameters.AddWithValue("$replyDeadlineUtc", ToDatabaseValue(replyDeadlineUtc));
        command.Parameters.AddWithValue("$workingDirectory", ToDatabaseValue(workingDirectory));
        command.Parameters.AddWithValue("$transcriptPath", ToDatabaseValue(transcriptPath));
        command.Parameters.AddWithValue("$receivedAtUtc", now);
        command.Parameters.AddWithValue("$status", WebhookEventStatuses.Pending);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    public async Task<StopFollowUpRequestAcceptedResult> InsertStopFollowUpAsync(string eventType, string reason, string? userInterfaceCulture, int? softLockedSessionCount, string? provider, string? providerName, string? sessionIdentifier, DateTimeOffset? startedAtUtc, DateTimeOffset? lastActivityAtUtc, DateTimeOffset? endedAtUtc, string? endReason, int? activeSessionCount, string? inputPromptPreview, string? lastAssistantMessage, int replyWaitSeconds, DateTimeOffset replyDeadlineUtc, string? workingDirectory, string? transcriptPath, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var publicIdentifier = Guid.NewGuid().ToString("N");
        var pollToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var pollTokenHash = ComputeHash(pollToken);
        var maximumDeadlineAtUtc = replyDeadlineUtc.AddSeconds(StopFollowUpTiming.MaximumReplyExtensionSeconds);

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        var webhookEventIdentifier = await InsertWebhookEventAsync(connection, transaction, eventType, reason, userInterfaceCulture, softLockedSessionCount, provider, providerName, sessionIdentifier, startedAtUtc, lastActivityAtUtc, endedAtUtc, endReason, activeSessionCount, inputPromptPreview, lastAssistantMessage, replyWaitSeconds, replyDeadlineUtc, workingDirectory, transcriptPath, now, cancellationToken);
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
                    MaximumDeadlineAtUtc,
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
                    $maximumDeadlineAtUtc,
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
            command.Parameters.AddWithValue("$maximumDeadlineAtUtc", maximumDeadlineAtUtc.ToString("O", CultureInfo.InvariantCulture));
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

    public async Task<bool> WaitForStopFollowUpConsumptionAsync(string publicIdentifier, int pollCycleCount, TimeSpan pollInterval, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(publicIdentifier)) return false;

        var normalizedPollCycleCount = Math.Max(0, pollCycleCount);
        for (var pollCycleIndex = 0; pollCycleIndex <= normalizedPollCycleCount; pollCycleIndex++)
        {
            if (await IsStopFollowUpConsumedAsync(publicIdentifier, cancellationToken)) return true;
            if (pollCycleIndex == normalizedPollCycleCount) return false;

            await Task.Delay(pollInterval, cancellationToken);
        }

        return false;
    }

    public async Task<StopFollowUpActionResult> ExtendStopFollowUpAsync(string publicIdentifier, int extendMinutes, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(publicIdentifier)) return new StopFollowUpActionResult(false, false, string.Empty, "The follow-up request was not found.", null, null, null);
        if (extendMinutes is < StopFollowUpTiming.MinimumExtensionMinutes or > StopFollowUpTiming.MaximumExtensionMinutes)
        {
            return new StopFollowUpActionResult(false, false, StopFollowUpRequestStatuses.Pending, $"extendMinutes must be between {StopFollowUpTiming.MinimumExtensionMinutes} and {StopFollowUpTiming.MaximumExtensionMinutes}.", null, null, null);
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        await ExpirePendingStopFollowUpRequestsAsync(connection, transaction, cancellationToken);

        var snapshot = await GetStopFollowUpActionSnapshotAsync(connection, transaction, publicIdentifier, cancellationToken);
        if (snapshot is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new StopFollowUpActionResult(false, false, string.Empty, "The follow-up request was not found.", null, null, null);
        }

        var status = snapshot.Value.Status;
        var deadlineAtUtc = snapshot.Value.DeadlineAtUtc;
        var maximumDeadlineAtUtc = snapshot.Value.MaximumDeadlineAtUtc ?? deadlineAtUtc.AddSeconds(StopFollowUpTiming.MaximumReplyExtensionSeconds);
        if (snapshot.Value.MaximumDeadlineAtUtc is null) await UpdateMaximumDeadlineAsync(connection, transaction, publicIdentifier, maximumDeadlineAtUtc, cancellationToken);

        if (!status.Equals(StopFollowUpRequestStatuses.Pending, StringComparison.Ordinal))
        {
            await transaction.CommitAsync(cancellationToken);
            return new StopFollowUpActionResult(false, false, status, CreateStopFollowUpStatusFailureMessage(status), deadlineAtUtc, maximumDeadlineAtUtc, snapshot.Value.ConsumedAtUtc);
        }

        var requestedDeadlineAtUtc = deadlineAtUtc.AddMinutes(extendMinutes);
        var nextDeadlineAtUtc = requestedDeadlineAtUtc > deadlineAtUtc ? requestedDeadlineAtUtc : deadlineAtUtc;
        if (nextDeadlineAtUtc > maximumDeadlineAtUtc) nextDeadlineAtUtc = maximumDeadlineAtUtc;
        var extended = nextDeadlineAtUtc > deadlineAtUtc;
        if (extended) await UpdateDeadlineAsync(connection, transaction, snapshot.Value.WebhookEventIdentifier, publicIdentifier, nextDeadlineAtUtc, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        var message = extended ? "Follow-up wait extended." : "The follow-up wait is already at the provider hook timeout limit.";
        return new StopFollowUpActionResult(true, extended, status, message, nextDeadlineAtUtc, maximumDeadlineAtUtc, snapshot.Value.ConsumedAtUtc);
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

    public async Task<StopFollowUpActionResult> GetStopFollowUpActionStateAsync(string publicIdentifier, string fallbackMessage, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        await ExpirePendingStopFollowUpRequestsAsync(connection, transaction, cancellationToken);
        var snapshot = await GetStopFollowUpActionSnapshotAsync(connection, transaction, publicIdentifier, cancellationToken);
        if (snapshot is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new StopFollowUpActionResult(false, false, string.Empty, "The follow-up request was not found.", null, null, null);
        }

        var maximumDeadlineAtUtc = snapshot.Value.MaximumDeadlineAtUtc ?? snapshot.Value.DeadlineAtUtc.AddSeconds(StopFollowUpTiming.MaximumReplyExtensionSeconds);
        if (snapshot.Value.MaximumDeadlineAtUtc is null) await UpdateMaximumDeadlineAsync(connection, transaction, publicIdentifier, maximumDeadlineAtUtc, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new StopFollowUpActionResult(true, false, snapshot.Value.Status, fallbackMessage, snapshot.Value.DeadlineAtUtc, maximumDeadlineAtUtc, snapshot.Value.ConsumedAtUtc);
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
            SELECT PollTokenHash, Status, ReplyText, ConsumedAtUtc, DeadlineAtUtc
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
        var deadlineAtUtc = GetTimestamp(reader, 4);
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
            Reply = status.Equals(StopFollowUpRequestStatuses.Answered, StringComparison.Ordinal) ? replyText : null,
            ExpiresAtUtc = deadlineAtUtc
        };
    }

    private async Task<bool> IsStopFollowUpConsumedAsync(string publicIdentifier, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ConsumedAtUtc
            FROM StopFollowUpRequests
            WHERE PublicIdentifier = $publicIdentifier;
            """;
        command.Parameters.AddWithValue("$publicIdentifier", publicIdentifier);
        var consumedAtUtc = await command.ExecuteScalarAsync(cancellationToken);
        return consumedAtUtc is string consumedTimestamp && !string.IsNullOrWhiteSpace(consumedTimestamp);
    }

    public async Task<WebhookEventListPage> ListRecentPageAsync(int pageSize, long? beforeWebhookEventIdentifier, CancellationToken cancellationToken)
    {
        var normalizedPageSize = Math.Clamp(pageSize, 1, 100);
        var fetchLimit = normalizedPageSize + 1;
        var events = new List<WebhookEventListItem>();
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            WITH SelectedEvents AS (
                SELECT Id
                FROM WebhookEvents
                WHERE $beforeWebhookEventIdentifier IS NULL
                    OR Id < $beforeWebhookEventIdentifier
                ORDER BY Id DESC
                LIMIT $fetchLimit
            )
            SELECT
                events.Id,
                events.EventType,
                events.Reason,
                events.SoftLockedSessionCount,
                events.Provider,
                events.ProviderName,
                events.SessionIdentifier,
                COALESCE(followUps.DeadlineAtUtc, events.ReplyDeadlineUtc),
                followUps.MaximumDeadlineAtUtc,
                SUBSTR(events.InputPromptPreview, 1, $previewCharacterLimit),
                SUBSTR(events.LastResponse, 1, $previewCharacterLimit),
                followUps.PublicIdentifier,
                followUps.Status,
                SUBSTR(followUps.ReplyText, 1, $previewCharacterLimit),
                events.ReceivedAtUtc,
                events.Status
            FROM SelectedEvents selectedEvents
            JOIN WebhookEvents events ON events.Id = selectedEvents.Id
            LEFT JOIN StopFollowUpRequests followUps ON followUps.WebhookEventId = events.Id
            ORDER BY events.Id DESC;
            """;
        command.Parameters.AddWithValue("$beforeWebhookEventIdentifier", beforeWebhookEventIdentifier.HasValue ? beforeWebhookEventIdentifier.Value : (object)DBNull.Value);
        command.Parameters.AddWithValue("$fetchLimit", fetchLimit);
        command.Parameters.AddWithValue("$previewCharacterLimit", 240);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var replyDeadlineAtUtc = GetNullableTimestamp(reader, 7);
            var maximumDeadlineAtUtc = GetNullableTimestamp(reader, 8) ?? replyDeadlineAtUtc?.AddSeconds(StopFollowUpTiming.MaximumReplyExtensionSeconds);
            events.Add(new WebhookEventListItem(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), GetNullableInt32(reader, 3), GetNullableString(reader, 4), GetNullableString(reader, 5), GetNullableString(reader, 6), WebhookTextPreview.Create(GetNullableString(reader, 9)), WebhookTextPreview.Create(GetNullableString(reader, 10)), replyDeadlineAtUtc, maximumDeadlineAtUtc, GetNullableString(reader, 11), GetNullableString(reader, 12), WebhookTextPreview.Create(GetNullableString(reader, 13)), GetTimestamp(reader, 14), reader.GetString(15)));
        }

        var hasMore = events.Count > normalizedPageSize;
        if (hasMore) events.RemoveAt(events.Count - 1);

        var nextBeforeWebhookEventIdentifier = hasMore && events.Count > 0 ? events[^1].WebhookEventIdentifier : (long?)null;
        return new WebhookEventListPage(events, hasMore, nextBeforeWebhookEventIdentifier);
    }

    public async Task<WebhookEventDetails?> GetDetailsAsync(long webhookEventIdentifier, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                events.Id,
                events.UserInterfaceCulture,
                events.StartedAtUtc,
                events.LastActivityAtUtc,
                events.EndedAtUtc,
                events.EndReason,
                events.ActiveSessionCount,
                events.WorkingDirectory,
                events.TranscriptPath,
                followUps.DeadlineAtUtc,
                followUps.MaximumDeadlineAtUtc,
                followUps.RepliedAtUtc,
                followUps.ConsumedAtUtc,
                events.ProcessedAtUtc,
                events.AttemptCount,
                COUNT(deliveries.Id) AS DeliveryCount,
                COALESCE(SUM(CASE WHEN deliveries.Status = $succeededStatus THEN 1 ELSE 0 END), 0) AS SuccessCount,
                COALESCE(SUM(CASE WHEN deliveries.Status = $permanentFailureStatus THEN 1 ELSE 0 END), 0) AS PermanentFailureCount,
                COALESCE(SUM(CASE WHEN deliveries.Status = $transientFailureStatus THEN 1 ELSE 0 END), 0) AS TransientFailureCount,
                events.LastError,
                events.LastResponse
            FROM WebhookEvents events
            LEFT JOIN NotificationDeliveries deliveries ON deliveries.WebhookEventId = events.Id
            LEFT JOIN StopFollowUpRequests followUps ON followUps.WebhookEventId = events.Id
            WHERE events.Id = $webhookEventIdentifier
            GROUP BY events.Id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$succeededStatus", DeliveryStatuses.Succeeded);
        command.Parameters.AddWithValue("$permanentFailureStatus", DeliveryStatuses.PermanentFailure);
        command.Parameters.AddWithValue("$transientFailureStatus", DeliveryStatuses.TransientFailure);
        command.Parameters.AddWithValue("$webhookEventIdentifier", webhookEventIdentifier);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        var deadlineAtUtc = GetNullableTimestamp(reader, 9);
        var maximumDeadlineAtUtc = GetNullableTimestamp(reader, 10) ?? deadlineAtUtc?.AddSeconds(StopFollowUpTiming.MaximumReplyExtensionSeconds);
        return new WebhookEventDetails(reader.GetInt64(0), GetNullableString(reader, 1), GetNullableTimestamp(reader, 2), GetNullableTimestamp(reader, 3), GetNullableTimestamp(reader, 4), GetNullableString(reader, 5), GetNullableInt32(reader, 6), GetNullableString(reader, 7), GetNullableString(reader, 8), deadlineAtUtc, maximumDeadlineAtUtc, GetNullableTimestamp(reader, 11), GetNullableTimestamp(reader, 12), GetNullableTimestamp(reader, 13), GetInt32(reader, 14), GetInt32(reader, 15), GetInt32(reader, 16), GetInt32(reader, 17), GetInt32(reader, 18), GetNullableString(reader, 19), GetNullableString(reader, 20));
    }

    private static async Task<StopFollowUpActionSnapshot?> GetStopFollowUpActionSnapshotAsync(SqliteConnection connection, SqliteTransaction transaction, string publicIdentifier, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT WebhookEventId, Status, DeadlineAtUtc, MaximumDeadlineAtUtc, ConsumedAtUtc
            FROM StopFollowUpRequests
            WHERE PublicIdentifier = $publicIdentifier;
            """;
        command.Parameters.AddWithValue("$publicIdentifier", publicIdentifier);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return new StopFollowUpActionSnapshot(reader.GetInt64(0), reader.GetString(1), GetTimestamp(reader, 2), GetNullableTimestamp(reader, 3), GetNullableTimestamp(reader, 4));
    }

    private static async Task UpdateDeadlineAsync(SqliteConnection connection, SqliteTransaction transaction, long webhookEventIdentifier, string publicIdentifier, DateTimeOffset deadlineAtUtc, CancellationToken cancellationToken)
    {
        using (var followUpCommand = connection.CreateCommand())
        {
            followUpCommand.Transaction = transaction;
            followUpCommand.CommandText =
                """
                UPDATE StopFollowUpRequests
                SET DeadlineAtUtc = $deadlineAtUtc
                WHERE PublicIdentifier = $publicIdentifier;
                """;
            followUpCommand.Parameters.AddWithValue("$deadlineAtUtc", deadlineAtUtc.ToString("O", CultureInfo.InvariantCulture));
            followUpCommand.Parameters.AddWithValue("$publicIdentifier", publicIdentifier);
            await followUpCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        using var eventCommand = connection.CreateCommand();
        eventCommand.Transaction = transaction;
        eventCommand.CommandText =
            """
            UPDATE WebhookEvents
            SET ReplyDeadlineUtc = $deadlineAtUtc
            WHERE Id = $webhookEventIdentifier;
            """;
        eventCommand.Parameters.AddWithValue("$deadlineAtUtc", deadlineAtUtc.ToString("O", CultureInfo.InvariantCulture));
        eventCommand.Parameters.AddWithValue("$webhookEventIdentifier", webhookEventIdentifier);
        await eventCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateMaximumDeadlineAsync(SqliteConnection connection, SqliteTransaction transaction, string publicIdentifier, DateTimeOffset maximumDeadlineAtUtc, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE StopFollowUpRequests
            SET MaximumDeadlineAtUtc = $maximumDeadlineAtUtc
            WHERE PublicIdentifier = $publicIdentifier
                AND MaximumDeadlineAtUtc IS NULL;
            """;
        command.Parameters.AddWithValue("$maximumDeadlineAtUtc", maximumDeadlineAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$publicIdentifier", publicIdentifier);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string CreateStopFollowUpStatusFailureMessage(string status)
        => status switch
        {
            StopFollowUpRequestStatuses.Answered => "A reply was already submitted for this follow-up request.",
            StopFollowUpRequestStatuses.Expired => "This follow-up request has already expired.",
            StopFollowUpRequestStatuses.Canceled => "This follow-up request was canceled.",
            _ => "The follow-up request cannot be changed."
        };

    private readonly record struct StopFollowUpActionSnapshot(long WebhookEventIdentifier, string Status, DateTimeOffset DeadlineAtUtc, DateTimeOffset? MaximumDeadlineAtUtc, DateTimeOffset? ConsumedAtUtc);

    private static string? GetNullableString(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static int? GetNullableInt32(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    private static int GetInt32(SqliteDataReader reader, int ordinal) => Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static DateTimeOffset GetTimestamp(SqliteDataReader reader, int ordinal) => DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static DateTimeOffset? GetNullableTimestamp(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : GetTimestamp(reader, ordinal);

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

    private static string ComputeHash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static object ToDatabaseValue(int? value) => value.HasValue ? value.Value : DBNull.Value;

    private static object ToDatabaseValue(DateTimeOffset? value) => value.HasValue ? value.Value.ToString("O", CultureInfo.InvariantCulture) : DBNull.Value;

    private static object ToDatabaseValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static async Task<long> InsertWebhookEventAsync(SqliteConnection connection, SqliteTransaction transaction, string eventType, string reason, string? userInterfaceCulture, int? softLockedSessionCount, string? provider, string? providerName, string? sessionIdentifier, DateTimeOffset? startedAtUtc, DateTimeOffset? lastActivityAtUtc, DateTimeOffset? endedAtUtc, string? endReason, int? activeSessionCount, string? inputPromptPreview, string? lastAssistantMessage, int? replyWaitSeconds, DateTimeOffset? replyDeadlineUtc, string? workingDirectory, string? transcriptPath, DateTimeOffset receivedAtUtc, CancellationToken cancellationToken)
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
        command.Parameters.AddWithValue("$lastResponse", ToDatabaseValue(lastAssistantMessage));
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
