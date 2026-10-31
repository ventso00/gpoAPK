using System.Globalization;
using ADOps.GpoManager.Core.Models;
using Microsoft.Data.Sqlite;

namespace ADOps.GpoManager.Infrastructure.Storage;

/// <summary>
/// Journal local des GPO appliquées par ADOps : une ligne par couple
/// (GPO, poste). La console du poste s'en sert pour afficher les stratégies
/// réellement déployées, les paramètres injectés et le groupe de filtrage.
/// </summary>
public sealed class GpoDeploymentRepository
{
    private readonly SqliteDatabase _database;

    public GpoDeploymentRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task UpsertAsync(GpoDeploymentRecord record)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO gpo_deployments (gpo_id, gpo_name, template_id, computer_name, computer_dn,
                                         ou_dn, filter_group, settings_json, deployed_utc)
            VALUES (@gpoId, @gpoName, @templateId, @computerName, @computerDn,
                    @ouDn, @filterGroup, @settingsJson, @deployedUtc)
            ON CONFLICT(gpo_id, computer_dn) DO UPDATE SET
                gpo_name = @gpoName, template_id = @templateId, computer_name = @computerName,
                ou_dn = @ouDn, filter_group = @filterGroup,
                settings_json = @settingsJson, deployed_utc = @deployedUtc;
            """;
        command.Parameters.AddWithValue("@gpoId", record.GpoId.ToString());
        command.Parameters.AddWithValue("@gpoName", record.GpoName);
        command.Parameters.AddWithValue("@templateId", record.TemplateId);
        command.Parameters.AddWithValue("@computerName", record.ComputerName);
        command.Parameters.AddWithValue("@computerDn", record.ComputerDistinguishedName);
        command.Parameters.AddWithValue("@ouDn", record.OuDistinguishedName);
        command.Parameters.AddWithValue("@filterGroup", record.FilterGroup ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@settingsJson", record.SettingsJson);
        command.Parameters.AddWithValue("@deployedUtc", record.DeployedUtc.ToUniversalTime().ToString("O"));
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GpoDeploymentRecord>> GetAllAsync()
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, gpo_id, gpo_name, template_id, computer_name, computer_dn,
                   ou_dn, filter_group, settings_json, deployed_utc
            FROM gpo_deployments
            ORDER BY deployed_utc DESC;
            """;

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var records = new List<GpoDeploymentRecord>();
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            records.Add(Read(reader));
        }

        return records;
    }

    public async Task<IReadOnlyList<GpoDeploymentRecord>> GetForComputerAsync(string computerKey)
    {
        if (string.IsNullOrWhiteSpace(computerKey))
        {
            return Array.Empty<GpoDeploymentRecord>();
        }

        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, gpo_id, gpo_name, template_id, computer_name, computer_dn,
                   ou_dn, filter_group, settings_json, deployed_utc
            FROM gpo_deployments
            WHERE computer_dn = @key OR computer_name = @key COLLATE NOCASE
            ORDER BY gpo_name COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("@key", computerKey.Trim());

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var records = new List<GpoDeploymentRecord>();
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            records.Add(Read(reader));
        }

        return records;
    }

    public async Task DeleteAsync(Guid gpoId, string computerDistinguishedName)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM gpo_deployments WHERE gpo_id = @gpoId AND computer_dn = @dn;";
        command.Parameters.AddWithValue("@gpoId", gpoId.ToString());
        command.Parameters.AddWithValue("@dn", computerDistinguishedName);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task DeleteForComputerAsync(string computerKey)
    {
        if (string.IsNullOrWhiteSpace(computerKey))
        {
            return;
        }

        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM gpo_deployments WHERE computer_dn = @key OR computer_name = @key COLLATE NOCASE;";
        command.Parameters.AddWithValue("@key", computerKey.Trim());
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static GpoDeploymentRecord Read(SqliteDataReader reader)
        => new()
        {
            Id = reader.GetInt64(0),
            GpoId = Guid.TryParse(reader.GetString(1), out var guid) ? guid : Guid.Empty,
            GpoName = reader.GetString(2),
            TemplateId = reader.GetString(3),
            ComputerName = reader.GetString(4),
            ComputerDistinguishedName = reader.GetString(5),
            OuDistinguishedName = reader.GetString(6),
            FilterGroup = reader.IsDBNull(7) ? null : reader.GetString(7),
            SettingsJson = reader.IsDBNull(8) ? "[]" : reader.GetString(8),
            DeployedUtc = DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        };
}
