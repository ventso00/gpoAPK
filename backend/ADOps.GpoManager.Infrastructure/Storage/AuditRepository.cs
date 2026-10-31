using System.Globalization;
using ADOps.GpoManager.Core.Models;
using Microsoft.Data.Sqlite;

namespace ADOps.GpoManager.Infrastructure.Storage;

/// <summary>Accès aux données d'audit (SQLite), avec les champs enrichis V1.</summary>
public sealed class AuditRepository
{
    private readonly SqliteDatabase _database;

    public AuditRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<long> InsertAsync(AuditEntry entry)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO audit_entries
                (timestamp_utc, user_name, action, target, result, message, details,
                 operation_id, server, domain, object_guid, targets,
                 initial_state, requested_state, error_code, duration_ms)
            VALUES
                ($timestamp, $userName, $action, $target, $result, $message, $details,
                 $operationId, $server, $domain, $objectGuid, $targets,
                 $initialState, $requestedState, $errorCode, $durationMs);
            SELECT last_insert_rowid();
            """;

        command.Parameters.AddWithValue("$timestamp", entry.TimestampUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$userName", entry.UserName);
        command.Parameters.AddWithValue("$action", entry.Action);
        command.Parameters.AddWithValue("$target", entry.Target);
        command.Parameters.AddWithValue("$result", entry.Result.ToString());
        command.Parameters.AddWithValue("$message", entry.Message);
        command.Parameters.AddWithValue("$details", entry.Details ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$operationId", entry.OperationId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$server", entry.Server ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$domain", entry.Domain ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$objectGuid", entry.ObjectGuid ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$targets", entry.Targets ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$initialState", entry.InitialState ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$requestedState", entry.RequestedState ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$errorCode", entry.ErrorCode ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$durationMs", entry.Duration is null ? (object)DBNull.Value : (long)entry.Duration.Value.TotalMilliseconds);

        return Convert.ToInt64(await command.ExecuteScalarAsync().ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task<IReadOnlyList<AuditEntry>> GetRecentAsync(int maxEntries)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, timestamp_utc, user_name, action, target, result, message, details,
                   operation_id, server, domain, object_guid, targets,
                   initial_state, requested_state, error_code, duration_ms
            FROM audit_entries
            ORDER BY id DESC
            LIMIT $maxEntries;
            """;
        command.Parameters.AddWithValue("$maxEntries", maxEntries);

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var entries = new List<AuditEntry>();
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            entries.Add(new AuditEntry
            {
                Id = reader.GetInt64(0),
                TimestampUtc = DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                UserName = reader.GetString(2),
                Action = reader.GetString(3),
                Target = reader.GetString(4),
                Result = ParseResult(reader.GetString(5)),
                Message = reader.GetString(6),
                Details = reader.IsDBNull(7) ? null : reader.GetString(7),
                OperationId = reader.IsDBNull(8) ? null : reader.GetString(8),
                Server = reader.IsDBNull(9) ? null : reader.GetString(9),
                Domain = reader.IsDBNull(10) ? null : reader.GetString(10),
                ObjectGuid = reader.IsDBNull(11) ? null : reader.GetString(11),
                Targets = reader.IsDBNull(12) ? null : reader.GetString(12),
                InitialState = reader.IsDBNull(13) ? null : reader.GetString(13),
                RequestedState = reader.IsDBNull(14) ? null : reader.GetString(14),
                ErrorCode = reader.IsDBNull(15) ? null : reader.GetString(15),
                Duration = reader.IsDBNull(16)
                    ? null
                    : TimeSpan.FromMilliseconds(reader.GetInt64(16)),
            });
        }

        entries.Reverse(); // ordre chronologique
        return entries;
    }

    public async Task<int> GetCountAsync()
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM audit_entries;";
        return Convert.ToInt32(await command.ExecuteScalarAsync().ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private static AuditResult ParseResult(string value) => value switch
    {
        "Success" => AuditResult.Success,
        "Warning" => AuditResult.Warning,
        "Failure" => AuditResult.Failure,
        _ => AuditResult.Information,
    };
}
