using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace LidGuard.Notifications.Data;

internal sealed class AuthenticationRefreshTokenStore(SqliteConnectionFactory connectionFactory)
{
    public async Task<AuthenticationRefreshTokenIssue> CreateAsync(TimeSpan lifetime, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var issue = CreateIssue(now, lifetime);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO AuthenticationRefreshTokens (
                TokenHash,
                CreatedAtUtc,
                ExpiresAtUtc,
                LastUsedAtUtc,
                RevokedAtUtc
            )
            VALUES ($tokenHash, $createdAtUtc, $expiresAtUtc, $lastUsedAtUtc, NULL);
            """;
        AddIssueParameters(command, issue, now);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return issue;
    }

    public async Task<AuthenticationRefreshTokenIssue?> RotateAsync(string refreshToken, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return null;

        var now = DateTimeOffset.UtcNow;
        var refreshTokenHash = HashToken(refreshToken);
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        var existingTokenIdentifier = await FindValidTokenIdentifierAsync(connection, transaction, refreshTokenHash, now, cancellationToken);
        if (existingTokenIdentifier is null)
        {
            transaction.Rollback();
            return null;
        }

        var issue = CreateIssue(now, lifetime);
        using (var revokeCommand = connection.CreateCommand())
        {
            revokeCommand.Transaction = transaction;
            revokeCommand.CommandText =
                """
                UPDATE AuthenticationRefreshTokens
                SET LastUsedAtUtc = $now,
                    RevokedAtUtc = $now
                WHERE Id = $identifier
                    AND RevokedAtUtc IS NULL;
                """;
            revokeCommand.Parameters.AddWithValue("$identifier", existingTokenIdentifier.Value);
            revokeCommand.Parameters.AddWithValue("$now", ToTimestamp(now));
            await revokeCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        using (var insertCommand = connection.CreateCommand())
        {
            insertCommand.Transaction = transaction;
            insertCommand.CommandText =
                """
                INSERT INTO AuthenticationRefreshTokens (
                    TokenHash,
                    CreatedAtUtc,
                    ExpiresAtUtc,
                    LastUsedAtUtc,
                    RevokedAtUtc
                )
                VALUES ($tokenHash, $createdAtUtc, $expiresAtUtc, $lastUsedAtUtc, NULL);
                """;
            AddIssueParameters(insertCommand, issue, now);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
        return issue;
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;

        var now = DateTimeOffset.UtcNow;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE AuthenticationRefreshTokens
            SET LastUsedAtUtc = $now,
                RevokedAtUtc = $now
            WHERE TokenHash = $tokenHash
                AND RevokedAtUtc IS NULL;
            """;
        command.Parameters.AddWithValue("$tokenHash", HashToken(refreshToken));
        command.Parameters.AddWithValue("$now", ToTimestamp(now));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long?> FindValidTokenIdentifierAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string refreshTokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT Id, ExpiresAtUtc
            FROM AuthenticationRefreshTokens
            WHERE TokenHash = $tokenHash
                AND RevokedAtUtc IS NULL;
            """;
        command.Parameters.AddWithValue("$tokenHash", refreshTokenHash);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        var expiresAtUtc = DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (expiresAtUtc <= now) return null;

        return reader.GetInt64(0);
    }

    private static AuthenticationRefreshTokenIssue CreateIssue(DateTimeOffset now, TimeSpan lifetime)
        => new(CreateToken(), now.Add(lifetime));

    private static string CreateToken()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static void AddIssueParameters(SqliteCommand command, AuthenticationRefreshTokenIssue issue, DateTimeOffset createdAtUtc)
    {
        command.Parameters.AddWithValue("$tokenHash", HashToken(issue.Token));
        command.Parameters.AddWithValue("$createdAtUtc", ToTimestamp(createdAtUtc));
        command.Parameters.AddWithValue("$expiresAtUtc", ToTimestamp(issue.ExpiresAtUtc));
        command.Parameters.AddWithValue("$lastUsedAtUtc", ToTimestamp(createdAtUtc));
    }

    private static string ToTimestamp(DateTimeOffset timestamp)
        => timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
