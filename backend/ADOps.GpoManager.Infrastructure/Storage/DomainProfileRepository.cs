using System.Globalization;
using ADOps.GpoManager.Core.Models;
using Microsoft.Data.Sqlite;

namespace ADOps.GpoManager.Infrastructure.Storage;

/// <summary>
/// Profils de connexion au domaine, sans aucun mot de passe (contrainte :
/// aucun credential stocké — l'application utilise l'identité Windows courante
/// ou redemande le mot de passe à chaque session).
/// </summary>
public sealed class DomainProfileRepository
{
    private readonly SqliteDatabase _database;

    public DomainProfileRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task SaveCurrentProfileAsync(DomainContext context)
    {
        using var connection = _database.CreateConnection();
        using var transaction = connection.BeginTransaction();

        using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "UPDATE domain_profiles SET is_current = 0;";
            await clear.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        using (var upsert = connection.CreateCommand())
        {
            upsert.Transaction = transaction;
            upsert.CommandText =
                """
                INSERT INTO domain_profiles (server_fqdn, domain_fqdn, domain_netbios, executor_mode, last_connected_utc, is_current)
                VALUES (@server, @domain, @netbios, @mode, @connected, 1)
                ON CONFLICT DO NOTHING;
                """;
            // Clé implicite : les colonnes sans contrainte UNIQUE → upsert par rowid ;
            // on utilise une clé métier explicite via sous-requête pour éviter les doublons.
            upsert.CommandText =
                """
                INSERT INTO domain_profiles (server_fqdn, domain_fqdn, domain_netbios, executor_mode, last_connected_utc, is_current)
                SELECT @server, @domain, @netbios, @mode, @connected, 1
                WHERE NOT EXISTS (
                    SELECT 1 FROM domain_profiles p
                    WHERE p.server_fqdn = @server AND p.domain_fqdn = @domain
                );
                UPDATE domain_profiles
                SET domain_netbios = @netbios, executor_mode = @mode,
                    last_connected_utc = @connected, is_current = 1
                WHERE server_fqdn = @server AND domain_fqdn = @domain;
                """;
            upsert.Parameters.AddWithValue("@server", context.ServerFqdn);
            upsert.Parameters.AddWithValue("@domain", context.DomainFqdn);
            upsert.Parameters.AddWithValue("@netbios", context.DomainNetbios ?? (object)DBNull.Value);
            upsert.Parameters.AddWithValue("@mode", context.ExecutorMode.ToString());
            upsert.Parameters.AddWithValue("@connected", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            await upsert.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        transaction.Commit();
    }

    public async Task<DomainProfile?> GetCurrentProfileAsync()
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT server_fqdn, domain_fqdn, domain_netbios, executor_mode, last_connected_utc
            FROM domain_profiles
            WHERE is_current = 1
            ORDER BY last_connected_utc DESC
            LIMIT 1;
            """;

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        if (!await reader.ReadAsync().ConfigureAwait(false))
        {
            return null;
        }

        return new DomainProfile
        {
            ServerFqdn = reader.GetString(0),
            DomainFqdn = reader.GetString(1),
            DomainNetbios = reader.IsDBNull(2) ? null : reader.GetString(2),
            ExecutorMode = reader.GetString(3) == "Remote" ? ExecutorMode.Remote : ExecutorMode.Local,
            LastConnectedUtc = DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        };
    }
}

/// <summary>Profil de connexion mémorisé (sans credential).</summary>
public sealed class DomainProfile
{
    public string ServerFqdn { get; init; } = string.Empty;
    public string DomainFqdn { get; init; } = string.Empty;
    public string? DomainNetbios { get; init; }
    public ExecutorMode ExecutorMode { get; init; }
    public DateTimeOffset LastConnectedUtc { get; init; }
}
