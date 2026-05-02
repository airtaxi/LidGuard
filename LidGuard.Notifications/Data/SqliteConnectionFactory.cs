using LidGuard.Notifications.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace LidGuard.Notifications.Data;

internal sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(IOptions<LidGuardNotificationsOptions> options)
    {
        DatabasePath = options.Value.DatabasePath;
        var databaseDirectory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrWhiteSpace(databaseDirectory)) Directory.CreateDirectory(databaseDirectory);

        var connectionStringBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };
        _connectionString = connectionStringBuilder.ToString();
    }

    public string DatabasePath { get; }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
