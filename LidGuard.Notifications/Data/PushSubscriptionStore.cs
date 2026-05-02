using System.Globalization;
using Microsoft.Data.Sqlite;

namespace LidGuard.Notifications.Data;

internal sealed class PushSubscriptionStore(SqliteConnectionFactory connectionFactory)
{
    public async Task UpsertAsync(string endpoint, string p256dhKey, string authenticationSecret, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Subscriptions (
                Endpoint,
                P256dh,
                Auth,
                CreatedAtUtc,
                UpdatedAtUtc,
                LastSeenAtUtc,
                IsActive,
                DeactivatedAtUtc,
                FailureCount,
                LastFailureAtUtc
            )
            VALUES ($endpoint, $p256dh, $auth, $now, $now, $now, 1, NULL, 0, NULL)
            ON CONFLICT(Endpoint) DO UPDATE SET
                P256dh = excluded.P256dh,
                Auth = excluded.Auth,
                UpdatedAtUtc = excluded.UpdatedAtUtc,
                LastSeenAtUtc = excluded.LastSeenAtUtc,
                IsActive = 1,
                DeactivatedAtUtc = NULL,
                FailureCount = 0,
                LastFailureAtUtc = NULL;
            """;
        command.Parameters.AddWithValue("$endpoint", endpoint);
        command.Parameters.AddWithValue("$p256dh", p256dhKey);
        command.Parameters.AddWithValue("$auth", authenticationSecret);
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeactivateByEndpointAsync(string endpoint, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Subscriptions
            SET IsActive = 0,
                DeactivatedAtUtc = $now,
                UpdatedAtUtc = $now
            WHERE Endpoint = $endpoint;
            """;
        command.Parameters.AddWithValue("$endpoint", endpoint);
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ActivePushSubscription>> ListActiveAsync(CancellationToken cancellationToken)
    {
        var subscriptions = new List<ActivePushSubscription>();
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, Endpoint, P256dh, Auth
            FROM Subscriptions
            WHERE IsActive = 1
            ORDER BY Id;
            """;
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            subscriptions.Add(new ActivePushSubscription(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return subscriptions;
    }

    public async Task<int> CountActiveAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Subscriptions WHERE IsActive = 1;";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public async Task MarkDeliverySucceededAsync(long subscriptionIdentifier, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Subscriptions
            SET FailureCount = 0,
                LastFailureAtUtc = NULL,
                LastSeenAtUtc = $now,
                UpdatedAtUtc = $now
            WHERE Id = $subscriptionIdentifier;
            """;
        command.Parameters.AddWithValue("$subscriptionIdentifier", subscriptionIdentifier);
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecordTransientFailureAsync(long subscriptionIdentifier, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Subscriptions
            SET FailureCount = FailureCount + 1,
                LastFailureAtUtc = $now,
                UpdatedAtUtc = $now
            WHERE Id = $subscriptionIdentifier;
            """;
        command.Parameters.AddWithValue("$subscriptionIdentifier", subscriptionIdentifier);
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeactivateForPermanentFailureAsync(long subscriptionIdentifier, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Subscriptions
            SET IsActive = 0,
                DeactivatedAtUtc = $now,
                FailureCount = FailureCount + 1,
                LastFailureAtUtc = $now,
                UpdatedAtUtc = $now
            WHERE Id = $subscriptionIdentifier;
            """;
        command.Parameters.AddWithValue("$subscriptionIdentifier", subscriptionIdentifier);
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
