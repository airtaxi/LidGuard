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
                Reason TEXT NOT NULL,
                SoftLockedSessionCount INTEGER NULL,
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
}
