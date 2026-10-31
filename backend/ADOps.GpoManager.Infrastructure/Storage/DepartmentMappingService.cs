using System.Globalization;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Services;
using Microsoft.Data.Sqlite;

namespace ADOps.GpoManager.Infrastructure.Storage;

/// <summary>
/// Mapping département ↔ OU, stocké dans la base locale de l'application.
/// Les OU restent dans AD DS : l'application ne crée, ne renomme, ne déplace
/// jamais d'OU pour simplifier l'interface.
/// </summary>
public sealed class DepartmentMappingService : IDepartmentMappingService
{
    private readonly SqliteDatabase _database;

    public DepartmentMappingService(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<IReadOnlyList<DepartmentMapping>> GetMappingsAsync()
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT m.department_name, m.ou_dn, m.updated_utc,
                   (SELECT COUNT(*) FROM computers c WHERE c.department_name = m.department_name)
            FROM department_mappings m
            ORDER BY m.department_name COLLATE NOCASE;
            """;

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var mappings = new List<DepartmentMapping>();
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            mappings.Add(new DepartmentMapping
            {
                DepartmentName = reader.GetString(0),
                OuDistinguishedName = reader.IsDBNull(1) ? null : reader.GetString(1),
                ComputerCount = reader.GetInt32(3),
                LastSeenUtc = DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            });
        }

        return mappings;
    }

    public async Task SaveMappingAsync(string departmentName, string? ouDistinguishedName)
    {
        if (string.IsNullOrWhiteSpace(departmentName))
        {
            throw new ArgumentException("Le nom du département est requis.", nameof(departmentName));
        }

        var name = departmentName.Trim();

        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO department_mappings (department_name, ou_dn, updated_utc)
            VALUES (@name, @ouDn, @updated)
            ON CONFLICT(department_name) DO UPDATE SET
                ou_dn = @ouDn, updated_utc = @updated;
            """;
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@ouDn", string.IsNullOrWhiteSpace(ouDistinguishedName) ? (object)DBNull.Value : ouDistinguishedName.Trim());
        command.Parameters.AddWithValue("@updated", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task RemoveMappingAsync(string departmentName)
    {
        if (string.IsNullOrWhiteSpace(departmentName))
        {
            return;
        }

        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM department_mappings WHERE department_name = @name;";
        command.Parameters.AddWithValue("@name", departmentName.Trim());
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);

        // Les ordinateurs du département supprimé redeviennent « Non classé ».
        using var clearConnection = _database.CreateConnection();
        using var clearCommand = clearConnection.CreateCommand();
        clearCommand.CommandText = "UPDATE computers SET department_name = NULL WHERE department_name = @name;";
        clearCommand.Parameters.AddWithValue("@name", departmentName.Trim());
        await clearCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
