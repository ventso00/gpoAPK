using System.Globalization;
using ADOps.GpoManager.Core.Models;
using Microsoft.Data.Sqlite;

namespace ADOps.GpoManager.Infrastructure.Storage;

/// <summary>
/// Persistance de la synchronisation : upsert des snapshots (OU, ordinateurs,
/// GPO, liens), détection des objets disparus, historique des runs.
/// Les identifiants LDAP/GUID sont les clés stables (pas les noms affichés).
/// </summary>
public sealed class InventoryRepository
{
    private readonly SqliteDatabase _database;

    public InventoryRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<SyncRunSummary> PersistSyncAsync(SyncResult result)
    {
        var startedUtc = DateTimeOffset.UtcNow;
        var runId = 0L;
        long computersRemoved = 0;
        int computersAdded = 0;
        int computersUpdated = 0;

        using (var connection = _database.CreateConnection())
        using (var transaction = connection.BeginTransaction())
        {
            runId = await InsertRunningRunAsync(connection, transaction, startedUtc);

            // Suppression des objets absents de ce run (last_seen antérieur au début du run).
            computersRemoved = await DeleteStaleAsync(connection, transaction, "computers", startedUtc);
            await DeleteStaleAsync(connection, transaction, "organizational_units", startedUtc);
            await DeleteStaleAsync(connection, transaction, "client_accounts", startedUtc);
            await DeleteStaleAsync(connection, transaction, "gpos", startedUtc);
            await DeleteStaleAsync(connection, transaction, "gpo_links", startedUtc);

            foreach (var computer in result.Computers)
            {
                var existed = await ComputerExistsAsync(connection, transaction, computer.DistinguishedName);
                if (existed)
                {
                    computersUpdated++;
                }
                else
                {
                    computersAdded++;
                }

                await UpsertComputerAsync(connection, transaction, computer, startedUtc);
            }

            foreach (var user in result.Users)
            {
                await UpsertClientAccountAsync(connection, transaction, user, startedUtc);
            }

            foreach (var ou in result.OrganizationalUnits)
            {
                await UpsertOuAsync(connection, transaction, ou, startedUtc);
            }

            foreach (var gpo in result.Gpos)
            {
                await UpsertGpoAsync(connection, transaction, gpo, startedUtc);
            }

            foreach (var link in result.Links)
            {
                await UpsertLinkAsync(connection, transaction, link, startedUtc);
            }

            // Recalcul du nombre d'ordinateurs par OU.
            await RecomputeOuComputerCountsAsync(connection, transaction);

            var state = result.IsPartial ? SyncState.Partial : SyncState.Succeeded;
            await FinalizeRunAsync(connection, transaction, runId, state, result, computersAdded, computersUpdated, (int)computersRemoved);

            transaction.Commit();
        }

        return new SyncRunSummary
        {
            Id = runId,
            StartedUtc = startedUtc,
            FinishedUtc = DateTimeOffset.UtcNow,
            State = result.IsPartial ? SyncState.Partial : SyncState.Succeeded,
            OrganizationalUnitsRead = result.OrganizationalUnits.Count,
            ComputersRead = result.Computers.Count,
            ComputersAdded = computersAdded,
            ComputersUpdated = computersUpdated,
            ComputersRemoved = (int)computersRemoved,
            UsersRead = result.Users.Count,
            GposRead = result.Gpos.Count,
            LinksRead = result.Links.Count,
            Warnings = result.Warnings,
        };
    }

    public async Task<IReadOnlyList<SyncRunSummary>> GetHistoryAsync(int maxRuns)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, started_utc, finished_utc, state, ous_read, computers_read,
                   computers_added, computers_updated, computers_removed, users_read,
                   gpos_read, links_read, warnings, error
            FROM sync_runs
            ORDER BY id DESC
            LIMIT @maxRuns;
            """;
        command.Parameters.AddWithValue("@maxRuns", maxRuns);

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var runs = new List<SyncRunSummary>();
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            runs.Add(new SyncRunSummary
            {
                Id = reader.GetInt64(0),
                StartedUtc = DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                FinishedUtc = reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                State = ParseSyncState(reader.GetString(3)),
                OrganizationalUnitsRead = reader.GetInt32(4),
                ComputersRead = reader.GetInt32(5),
                ComputersAdded = reader.GetInt32(6),
                ComputersUpdated = reader.GetInt32(7),
                ComputersRemoved = reader.GetInt32(8),
                UsersRead = reader.GetInt32(9),
                GposRead = reader.GetInt32(10),
                LinksRead = reader.GetInt32(11),
                Warnings = ParseStringList(reader, 12),
                Error = reader.IsDBNull(13) ? null : reader.GetString(13),
            });
        }

        runs.Reverse();
        return runs;
    }

    private static async Task<long> InsertRunningRunAsync(SqliteConnection connection, SqliteTransaction transaction, DateTimeOffset startedUtc)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO sync_runs (started_utc, state)
            VALUES (@startedUtc, 'Running');
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("@startedUtc", startedUtc.ToString("O", CultureInfo.InvariantCulture));
        return Convert.ToInt64(await command.ExecuteScalarAsync().ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private static async Task<int> DeleteStaleAsync(SqliteConnection connection, SqliteTransaction transaction, string table, DateTimeOffset syncStart)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM " + table + " WHERE last_seen_utc < @syncStart;";
        command.Parameters.AddWithValue("@syncStart", syncStart.ToString("O", CultureInfo.InvariantCulture));
        return command.ExecuteNonQuery();
    }

    private static async Task<bool> ComputerExistsAsync(SqliteConnection connection, SqliteTransaction transaction, string distinguishedName)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM computers WHERE distinguished_name = @dn;";
        command.Parameters.AddWithValue("@dn", distinguishedName);
        return await command.ExecuteScalarAsync().ConfigureAwait(false) is not null;
    }

    private static async Task UpsertComputerAsync(SqliteConnection connection, SqliteTransaction transaction, ComputerInfo computer, DateTimeOffset seenUtc)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO computers (distinguished_name, name, sam_account_name, ou_dn, operating_system,
                                   operating_system_version, enabled, last_logon_utc, ip_ad, last_seen_utc)
            VALUES (@dn, @name, @sam, @ouDn, @os, @osVersion, @enabled, @lastLogon, @ipAd, @seen)
            ON CONFLICT(distinguished_name) DO UPDATE SET
                name = @name, sam_account_name = @sam, ou_dn = @ouDn, operating_system = @os,
                operating_system_version = @osVersion, enabled = @enabled, last_logon_utc = @lastLogon,
                ip_ad = COALESCE(@ipAd, ip_ad),
                last_seen_utc = @seen;
            """;
        command.Parameters.AddWithValue("@dn", computer.DistinguishedName);
        command.Parameters.AddWithValue("@name", computer.Name);
        command.Parameters.AddWithValue("@sam", computer.SamAccountName ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@ouDn", computer.OuDistinguishedName ?? string.Empty);
        command.Parameters.AddWithValue("@os", computer.OperatingSystem ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@osVersion", computer.OperatingSystemVersion ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@enabled", computer.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("@lastLogon", computer.LastLogonUtc?.ToString("O", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@ipAd", computer.IpAddress ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@seen", seenUtc.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Enregistre un compte utilisateur (client) dans le cache local.
    /// La clé est le DistinguishedName : le nom de connexion peut changer
    /// sans créer de doublon.
    /// </summary>
    private static async Task UpsertClientAccountAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        UserInfo user,
        DateTimeOffset seenUtc)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO client_accounts (distinguished_name, sam_account_name, display_name, given_name,
                                         surname, email, title, department, ou_dn, enabled,
                                         is_administrator, account_locked, password_never_expires,
                                         last_logon_utc, last_seen_utc)
            VALUES (@dn, @sam, @display, @given, @surname, @email, @title, @department, @ouDn, @enabled,
                    @isAdmin, @locked, @neverExpires, @lastLogon, @seen)
            ON CONFLICT(distinguished_name) DO UPDATE SET
                sam_account_name = @sam, display_name = @display, given_name = @given,
                surname = @surname, email = @email, title = @title, department = @department,
                ou_dn = @ouDn, enabled = @enabled, is_administrator = @isAdmin,
                account_locked = @locked, password_never_expires = @neverExpires,
                last_logon_utc = @lastLogon, last_seen_utc = @seen;
            """;
        command.Parameters.AddWithValue("@dn", user.DistinguishedName);
        command.Parameters.AddWithValue("@sam", user.SamAccountName);
        command.Parameters.AddWithValue("@display", user.DisplayName ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@given", user.GivenName ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@surname", user.Surname ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@email", user.Email ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@title", user.Title ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@department", user.Department ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@ouDn", user.OuDistinguishedName ?? string.Empty);
        command.Parameters.AddWithValue("@enabled", user.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("@isAdmin", user.IsAdministrator ? 1 : 0);
        command.Parameters.AddWithValue("@locked", user.AccountLocked ? 1 : 0);
        command.Parameters.AddWithValue("@neverExpires", user.PasswordNeverExpires ? 1 : 0);
        command.Parameters.AddWithValue("@lastLogon", user.LastLogonUtc?.ToString("O", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@seen", seenUtc.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task UpsertOuAsync(SqliteConnection connection, SqliteTransaction transaction, OuInfo ou, DateTimeOffset seenUtc)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO organizational_units (distinguished_name, name, canonical_name, parent_dn, last_seen_utc)
            VALUES (@dn, @name, @canonical, @parentDn, @seen)
            ON CONFLICT(distinguished_name) DO UPDATE SET
                name = @name, canonical_name = @canonical, parent_dn = @parentDn, last_seen_utc = @seen;
            """;
        command.Parameters.AddWithValue("@dn", ou.DistinguishedName);
        command.Parameters.AddWithValue("@name", ou.Name);
        command.Parameters.AddWithValue("@canonical", ou.CanonicalName ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@parentDn", ou.ParentDistinguishedName ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@seen", seenUtc.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task UpsertGpoAsync(SqliteConnection connection, SqliteTransaction transaction, GpoSummary gpo, DateTimeOffset seenUtc)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO gpos (gpo_id, display_name, description, gpo_status, creation_time_utc,
                              modification_time_utc, domain_name, last_seen_utc)
            VALUES (@id, @name, @description, @status, @created, @modified, @domain, @seen)
            ON CONFLICT(gpo_id) DO UPDATE SET
                display_name = @name, description = @description, gpo_status = @status,
                creation_time_utc = @created, modification_time_utc = @modified,
                domain_name = @domain, last_seen_utc = @seen;
            """;
        command.Parameters.AddWithValue("@id", gpo.Id.ToString());
        command.Parameters.AddWithValue("@name", gpo.DisplayName);
        command.Parameters.AddWithValue("@description", gpo.Description ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@status", gpo.Status.ToString());
        command.Parameters.AddWithValue("@created", gpo.CreationTime?.ToString("O", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@modified", gpo.ModificationTime?.ToString("O", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@domain", gpo.DomainName ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@seen", seenUtc.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task UpsertLinkAsync(SqliteConnection connection, SqliteTransaction transaction, GpoLinkInfo link, DateTimeOffset seenUtc)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO gpo_links (gpo_id, target_dn, target_type, [order], enabled, last_seen_utc)
            VALUES (@gpoId, @targetDn, @targetType, @order, @enabled, @seen)
            ON CONFLICT(gpo_id, target_dn) DO UPDATE SET
                target_type = @targetType, [order] = @order, enabled = @enabled, last_seen_utc = @seen;
            """;
        command.Parameters.AddWithValue("@gpoId", link.GpoId.ToString());
        command.Parameters.AddWithValue("@targetDn", link.TargetDistinguishedName);
        command.Parameters.AddWithValue("@targetType", link.TargetType.ToString());
        command.Parameters.AddWithValue("@order", link.Order);
        command.Parameters.AddWithValue("@enabled", link.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("@seen", seenUtc.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task RecomputeOuComputerCountsAsync(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE organizational_units
            SET computer_count = (
                SELECT COUNT(*) FROM computers
                WHERE computers.ou_dn = organizational_units.distinguished_name
            );
            """;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task FinalizeRunAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long runId,
        SyncState state,
        SyncResult result,
        int computersAdded,
        int computersUpdated,
        int computersRemoved)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE sync_runs SET
                finished_utc = @finishedUtc,
                state = @state,
                ous_read = @ousRead,
                computers_read = @computersRead,
                computers_added = @computersAdded,
                computers_updated = @computersUpdated,
                computers_removed = @computersRemoved,
                users_read = @usersRead,
                gpos_read = @gposRead,
                links_read = @linksRead,
                warnings = @warnings
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("@finishedUtc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@state", state.ToString());
        command.Parameters.AddWithValue("@ousRead", result.OrganizationalUnits.Count);
        command.Parameters.AddWithValue("@computersRead", result.Computers.Count);
        command.Parameters.AddWithValue("@computersAdded", computersAdded);
        command.Parameters.AddWithValue("@computersUpdated", computersUpdated);
        command.Parameters.AddWithValue("@computersRemoved", computersRemoved);
        command.Parameters.AddWithValue("@usersRead", result.Users.Count);
        command.Parameters.AddWithValue("@gposRead", result.Gpos.Count);
        command.Parameters.AddWithValue("@linksRead", result.Links.Count);
        command.Parameters.AddWithValue("@warnings", result.Warnings.Count == 0
            ? (object)DBNull.Value
            : string.Join(" | ", result.Warnings));
        command.Parameters.AddWithValue("@id", runId);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static SyncState ParseSyncState(string value) => value switch
    {
        "Succeeded" => SyncState.Succeeded,
        "Partial" => SyncState.Partial,
        "Failed" => SyncState.Failed,
        "Running" => SyncState.Running,
        _ => SyncState.NotRun,
    };

    private static IReadOnlyList<string> ParseStringList(SqliteDataReader reader, int column)
    {
        if (reader.IsDBNull(column))
        {
            return Array.Empty<string>();
        }

        var raw = reader.GetString(column);
        return string.IsNullOrEmpty(raw)
            ? Array.Empty<string>()
            : raw.Split(" | ", StringSplitOptions.RemoveEmptyEntries);
    }
}
