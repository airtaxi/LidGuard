using Microsoft.Data.Sqlite;

namespace LidGuard.Notifications.Data;

internal sealed class NotificationDatabaseInitializer(SqliteConnectionFactory connectionFactory)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        foreach (var commandText in CreateSchemaCommands())
        {
            using var command = connection.CreateCommand();
            command.CommandText = commandText;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await EnsureWebhookEventsColumnsAsync(connection, cancellationToken);
    }

    private static async Task EnsureWebhookEventsColumnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var columnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var inspectCommand = connection.CreateCommand();
        inspectCommand.CommandText = "PRAGMA table_info(WebhookEvents);";
        using var reader = await inspectCommand.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) columnNames.Add(reader.GetString(1));

        await EnsureWebhookEventsColumnAsync(connection, columnNames, "UserInterfaceCulture", "TEXT NULL", cancellationToken);
        await EnsureWebhookEventsColumnAsync(connection, columnNames, "ReplyWaitSeconds", "INTEGER NULL", cancellationToken);
        await EnsureWebhookEventsColumnAsync(connection, columnNames, "ReplyDeadlineUtc", "TEXT NULL", cancellationToken);
    }

    private static IReadOnlyList<string> CreateSchemaCommands()
        =>
        [
            "PRAGMA journal_mode = WAL;",
            """
            CREATE TABLE IF NOT EXISTS Subscriptions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Endpoint TEXT NOT NULL UNIQUE,
                P256dh TEXT NOT NULL,
                Auth TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                LastSeenAtUtc TEXT NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                DeactivatedAtUtc TEXT NULL,
                FailureCount INTEGER NOT NULL DEFAULT 0,
                LastFailureAtUtc TEXT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS WebhookEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                EventType TEXT NOT NULL,
                Reason TEXT NOT NULL,
                UserInterfaceCulture TEXT NULL,
                SoftLockedSessionCount INTEGER NULL,
                Provider TEXT NULL,
                ProviderName TEXT NULL,
                SessionIdentifier TEXT NULL,
                StartedAtUtc TEXT NULL,
                LastActivityAtUtc TEXT NULL,
                EndedAtUtc TEXT NULL,
                EndReason TEXT NULL,
                ActiveSessionCount INTEGER NULL,
                InputPromptPreview TEXT NULL,
                LastResponse TEXT NULL,
                ReplyWaitSeconds INTEGER NULL,
                ReplyDeadlineUtc TEXT NULL,
                WorkingDirectory TEXT NULL,
                TranscriptPath TEXT NULL,
                ReceivedAtUtc TEXT NOT NULL,
                ProcessedAtUtc TEXT NULL,
                Status TEXT NOT NULL DEFAULT 'Pending',
                AttemptCount INTEGER NOT NULL DEFAULT 0,
                LastError TEXT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS NotificationDeliveries (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                WebhookEventId INTEGER NOT NULL,
                SubscriptionId INTEGER NOT NULL,
                Status TEXT NOT NULL,
                HttpStatusCode INTEGER NULL,
                Error TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (WebhookEventId) REFERENCES WebhookEvents(Id) ON DELETE CASCADE,
                FOREIGN KEY (SubscriptionId) REFERENCES Subscriptions(Id) ON DELETE CASCADE
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS AuthenticationRefreshTokens (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TokenHash TEXT NOT NULL UNIQUE,
                CreatedAtUtc TEXT NOT NULL,
                ExpiresAtUtc TEXT NOT NULL,
                LastUsedAtUtc TEXT NOT NULL,
                RevokedAtUtc TEXT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS StopFollowUpRequests (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                WebhookEventId INTEGER NOT NULL UNIQUE,
                PublicIdentifier TEXT NOT NULL UNIQUE,
                PollTokenHash TEXT NOT NULL,
                Status TEXT NOT NULL,
                ReplyText TEXT NULL,
                DeadlineAtUtc TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                RepliedAtUtc TEXT NULL,
                ConsumedAtUtc TEXT NULL,
                FOREIGN KEY (WebhookEventId) REFERENCES WebhookEvents(Id) ON DELETE CASCADE
            );
            """,
            "CREATE INDEX IF NOT EXISTS IX_Subscriptions_IsActive ON Subscriptions(IsActive);",
            "CREATE INDEX IF NOT EXISTS IX_WebhookEvents_Status_Id ON WebhookEvents(Status, Id);",
            "CREATE INDEX IF NOT EXISTS IX_NotificationDeliveries_WebhookEventId ON NotificationDeliveries(WebhookEventId);",
            "CREATE INDEX IF NOT EXISTS IX_StopFollowUpRequests_PublicIdentifier ON StopFollowUpRequests(PublicIdentifier);",
            "CREATE INDEX IF NOT EXISTS IX_StopFollowUpRequests_Status_DeadlineAtUtc ON StopFollowUpRequests(Status, DeadlineAtUtc);",
            "CREATE INDEX IF NOT EXISTS IX_AuthenticationRefreshTokens_TokenHash ON AuthenticationRefreshTokens(TokenHash);",
            "CREATE INDEX IF NOT EXISTS IX_AuthenticationRefreshTokens_ExpiresAtUtc ON AuthenticationRefreshTokens(ExpiresAtUtc);"
        ];

    private static async Task EnsureWebhookEventsColumnAsync(
        SqliteConnection connection,
        ISet<string> existingColumnNames,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        if (existingColumnNames.Contains(columnName)) return;

        using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE WebhookEvents ADD COLUMN {columnName} {columnDefinition};";
        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
        existingColumnNames.Add(columnName);
    }
}
