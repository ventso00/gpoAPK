using System.Globalization;
using ADOps.GpoManager.Core.Models;
using Microsoft.Data.Sqlite;

namespace ADOps.GpoManager.Infrastructure.Storage;

/// <summary>
/// Cible de déploiement gérée localement : les comptes clients et les postes
/// que l'administrateur a retenus pour l'application d'une GPO.
/// <para>
/// Ces cibles sont un **outil de travail local** : elles permettent d'ajouter,
/// de modifier et de retirer une cible sans toucher à Active Directory. Le
/// déploiement lui-même reste piloté par l'annuaire.
/// </para>
/// </summary>
public sealed class AssistantTargetRepository
{
    /// <summary>Nature de la cible : compte utilisateur ou poste.</summary>
    public const string KindAccount = "account";

    /// <summary>Nature de la cible : poste.</summary>
    public const string KindComputer = "computer";

    private readonly SqliteDatabase _database;

    public AssistantTargetRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<IReadOnlyList<AssistantTarget>> GetAllAsync()
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT kind, distinguished_name, display_name, sam_account_name, ou_dn, is_selected, note, last_seen_utc
            FROM assistant_targets
            ORDER BY kind, display_name COLLATE NOCASE;
            """;

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var targets = new List<AssistantTarget>();
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            targets.Add(new AssistantTarget
            {
                Kind = reader.GetString(0),
                DistinguishedName = reader.GetString(1),
                DisplayName = reader.IsDBNull(2) ? null : reader.GetString(2),
                SamAccountName = reader.IsDBNull(3) ? null : reader.GetString(3),
                OuDistinguishedName = reader.IsDBNull(4) ? null : reader.GetString(4),
                IsSelected = reader.GetInt32(5) == 1,
                Note = reader.IsDBNull(6) ? null : reader.GetString(6),
                LastSeenUtc = DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            });
        }

        return targets;
    }

    /// <summary>Ajoute une cible (ou la met à jour si elle existe déjà).</summary>
    public async Task SaveAsync(AssistantTarget target)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO assistant_targets (kind, distinguished_name, display_name, sam_account_name,
                                          ou_dn, is_selected, note, last_seen_utc)
            VALUES (@kind, @dn, @display, @sam, @ouDn, @selected, @note, @seen)
            ON CONFLICT(kind, distinguished_name) DO UPDATE SET
                display_name = @display, sam_account_name = @sam, ou_dn = @ouDn,
                is_selected = @selected, note = @note, last_seen_utc = @seen;
            """;
        command.Parameters.AddWithValue("@kind", target.Kind);
        command.Parameters.AddWithValue("@dn", target.DistinguishedName);
        command.Parameters.AddWithValue("@display", target.DisplayName ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@sam", target.SamAccountName ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@ouDn", target.OuDistinguishedName ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@selected", target.IsSelected ? 1 : 0);
        command.Parameters.AddWithValue("@note", target.Note ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@seen", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <summary>Supprime une cible.</summary>
    public async Task DeleteAsync(string kind, string distinguishedName)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM assistant_targets WHERE kind = @kind AND distinguished_name = @dn;";
        command.Parameters.AddWithValue("@kind", kind);
        command.Parameters.AddWithValue("@dn", distinguishedName);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <summary>Supprime toutes les cibles d'un type donné.</summary>
    public async Task DeleteAllAsync(string kind)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM assistant_targets WHERE kind = @kind;";
        command.Parameters.AddWithValue("@kind", kind);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
