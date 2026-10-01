using Microsoft.Data.Sqlite;

namespace DAP.Data.Sqlite;

public sealed class SqliteConnectionFactory
{
    private readonly SqliteDatabaseOptions _options;

    public SqliteConnectionFactory(SqliteDatabaseOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_options.DatabasePath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("SQLite database directory could not be resolved.");

        Directory.CreateDirectory(directory);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _options.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true
        };

        var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
