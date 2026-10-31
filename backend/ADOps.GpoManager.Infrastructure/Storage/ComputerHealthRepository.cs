using System.Globalization;
using ADOps.GpoManager.Core.Models;
using Microsoft.Data.Sqlite;

namespace ADOps.GpoManager.Infrastructure.Storage;

/// <summary>Persistance des résultats de disponibilité des ordinateurs du parc.</summary>
public sealed class ComputerHealthRepository
{
    private readonly SqliteDatabase _database;

    public ComputerHealthRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task SaveHealthAsync(IReadOnlyList<ComputerHealthInfo> healthResults)
    {
        using var connection = _database.CreateConnection();
        using var transaction = connection.BeginTransaction();

        foreach (var health in healthResults)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO computer_health (computer_dn, state, method, dns_resolved, dns_ip,
                                             winrm_available, checked_utc, error)
                VALUES (@dn, @state, @method, @dnsResolved, @dnsIp, @winrm, @checked, @error)
                ON CONFLICT(computer_dn) DO UPDATE SET
                    state = @state, method = @method, dns_resolved = @dnsResolved,
                    dns_ip = @dnsIp, winrm_available = @winrm, checked_utc = @checked, error = @error;
                """;
            command.Parameters.AddWithValue("@dn", health.ComputerDistinguishedName);
            command.Parameters.AddWithValue("@state", health.State.ToString());
            command.Parameters.AddWithValue("@method", health.Method.ToString());
            command.Parameters.AddWithValue("@dnsResolved", health.DnsResolved ? 1 : 0);
            command.Parameters.AddWithValue("@dnsIp", health.DnsIpAddress ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@winrm", health.WinRmAvailable ? 1 : 0);
            command.Parameters.AddWithValue("@checked", health.CheckedAtUtc.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("@error", health.Error ?? (object)DBNull.Value);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        transaction.Commit();
    }
}
