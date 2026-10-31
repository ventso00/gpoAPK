using Microsoft.Data.Sqlite;

namespace ADOps.GpoManager.Infrastructure.Storage;

/// <summary>Base de données SQLite locale : création du schéma et fabrique de connexions.</summary>
public sealed class SqliteDatabase : IDisposable
{
    private readonly string _connectionString;

    public SqliteDatabase(string databaseFile)
    {
        var directory = Path.GetDirectoryName(databaseFile);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = "Data Source=" + databaseFile;
        InitializeSchema();
    }

    /// <summary>Ouvre une connexion dédiée (à libérer par l'appelant via using).</summary>
    public SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void InitializeSchema()
    {
        using var connection = CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS audit_entries (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp_utc TEXT NOT NULL,
                user_name TEXT NOT NULL,
                action TEXT NOT NULL,
                target TEXT NOT NULL,
                result TEXT NOT NULL,
                message TEXT NOT NULL,
                details TEXT
            );
            CREATE INDEX IF NOT EXISTS ix_audit_entries_timestamp
                ON audit_entries (timestamp_utc DESC);
            """;
        command.ExecuteNonQuery();

        // Schéma V1 + migration de l'audit (idempotent).
        DatabaseSchema.Initialize(this);
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
