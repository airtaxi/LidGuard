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

        await EnsureWebhookEventColumnsAsync(connection, cancellationToken);
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
                EventType TEXT NOT NULL DEFAULT 'PreSuspend',
                Reason TEXT NOT NULL,
                SoftLockedSessionCount INTEGER NULL,
                Provider TEXT NULL,
                ProviderName TEXT NULL,
                SessionIdentifier TEXT NULL,
                StartedAtUtc TEXT NULL,
                LastActivityAtUtc TEXT NULL,
                EndedAtUtc TEXT NULL,
                EndReason TEXT NULL,
                ActiveSessionCount INTEGER NULL,
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
            "CREATE INDEX IF NOT EXISTS IX_Subscriptions_IsActive ON Subscriptions(IsActive);",
            "CREATE INDEX IF NOT EXISTS IX_WebhookEvents_Status_Id ON WebhookEvents(Status, Id);",
            "CREATE INDEX IF NOT EXISTS IX_NotificationDeliveries_WebhookEventId ON NotificationDeliveries(WebhookEventId);"
        ];

    private static async Task EnsureWebhookEventColumnsAsync(Microsoft.Data.Sqlite.SqliteConnection connection, CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(connection, "WebhookEvents", "EventType", "TEXT NOT NULL DEFAULT 'PreSuspend'", cancellationToken);
        await EnsureColumnAsync(connection, "WebhookEvents", "Provider", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(connection, "WebhookEvents", "ProviderName", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(connection, "WebhookEvents", "SessionIdentifier", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(connection, "WebhookEvents", "StartedAtUtc", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(connection, "WebhookEvents", "LastActivityAtUtc", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(connection, "WebhookEvents", "EndedAtUtc", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(connection, "WebhookEvents", "EndReason", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(connection, "WebhookEvents", "ActiveSessionCount", "INTEGER NULL", cancellationToken);
        await EnsureColumnAsync(connection, "WebhookEvents", "WorkingDirectory", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(connection, "WebhookEvents", "TranscriptPath", "TEXT NULL", cancellationToken);
    }

    private static async Task EnsureColumnAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        string tableName,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        if (await HasColumnAsync(connection, tableName, columnName, cancellationToken)) return;

        using var command = connection.CreateCommand();
        command.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> HasColumnAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.GetString(1).Equals(columnName, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }
}
