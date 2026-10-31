using System.Globalization;
using ADOps.GpoManager.Core.Models;
using Microsoft.Data.Sqlite;

namespace ADOps.GpoManager.Infrastructure.Storage;

/// <summary>
/// Requêtes de lecture du parc (tableau de bord, départements, ordinateurs,
/// santé, liens, rapports RSoP, sauvegardes). Le cache local n'est jamais
/// présenté comme la source de vérité : chaque donnée porte son horodatage.
/// </summary>
public sealed class InventoryQueryRepository
{
    private readonly SqliteDatabase _database;

    public InventoryQueryRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<DashboardSnapshot> GetDashboardSnapshotAsync()
    {
        using var connection = _database.CreateConnection();

        var computers = await ScalarIntAsync(connection, "SELECT COUNT(*) FROM computers;");
        var online = await ScalarIntAsync(connection,
            "SELECT COUNT(*) FROM computer_health WHERE state IN ('Online','OnlineWithoutWinRm');");
        var unreachable = await ScalarIntAsync(connection,
            "SELECT COUNT(*) FROM computer_health WHERE state = 'DnsUnresolved';");
        var unknown = computers - online - unreachable;
        var ous = await ScalarIntAsync(connection, "SELECT COUNT(*) FROM organizational_units;");
        var gpos = await ScalarIntAsync(connection, "SELECT COUNT(*) FROM gpos;");
        var links = await ScalarIntAsync(connection, "SELECT COUNT(*) FROM gpo_links;");
        var accounts = await ScalarIntAsync(connection, "SELECT COUNT(*) FROM client_accounts;");
        var adminAccounts = await ScalarIntAsync(connection, "SELECT COUNT(*) FROM client_accounts WHERE is_administrator = 1;");
        var departments = await ScalarIntAsync(connection, "SELECT COUNT(*) FROM department_mappings;");
        var mappedComputers = await ScalarIntAsync(connection,
            "SELECT COUNT(*) FROM computers WHERE department_name IS NOT NULL;");
        var lastSync = await ScalarTextAsync(connection,
            "SELECT MAX(finished_utc) FROM sync_runs WHERE state IN ('Succeeded','Partial');");
        var lastBackup = await ScalarTextAsync(connection,
            "SELECT MAX(created_utc) FROM backup_records;");

        return new DashboardSnapshot
        {
            TotalComputers = computers,
            OnlineComputers = online,
            UnreachableComputers = unreachable,
            UnknownComputers = Math.Max(0, unknown),
            OrganizationalUnits = ous,
            Gpos = gpos,
            GpoLinks = links,
            ClientAccounts = accounts,
            AdministratorAccounts = adminAccounts,
            Departments = departments,
            UnmappedComputers = Math.Max(0, computers - mappedComputers),
            LastSyncUtc = ParseDateTimeOffset(lastSync),
            LastBackupUtc = ParseDateTimeOffset(lastBackup),
        };
    }

    public async Task<IReadOnlyList<DepartmentMapping>> GetDepartmentsAsync()
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT m.department_name, m.ou_dn, m.updated_utc,
                   (SELECT COUNT(*) FROM computers c WHERE c.department_name = m.department_name),
                   (SELECT COUNT(*) FROM computer_health h
                     JOIN computers c2 ON h.computer_dn = c2.distinguished_name
                     WHERE c2.department_name = m.department_name
                       AND h.state IN ('Online','OnlineWithoutWinRm'))
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

    public async Task<IReadOnlyList<OuMappingRow>> GetOuMappingsAsync()
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ou.distinguished_name, ou.name, ou.computer_count, m.department_name
            FROM organizational_units ou
            LEFT JOIN department_mappings m ON m.ou_dn = ou.distinguished_name
            ORDER BY ou.name COLLATE NOCASE;
            """;

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var rows = new List<OuMappingRow>();
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            rows.Add(new OuMappingRow
            {
                DistinguishedName = reader.GetString(0),
                Name = reader.GetString(1),
                ComputerCount = reader.GetInt32(2),
                DepartmentName = reader.IsDBNull(3) ? null : reader.GetString(3),
            });
        }

        return rows;
    }

    /// <summary>Sélection des colonnes d'un poste, commune à la liste et à la fiche détaillée.</summary>
    private const string ComputerSelectColumns =
        """
        SELECT c.distinguished_name, c.name, c.ou_dn, c.operating_system,
               c.operating_system_version, c.enabled, c.last_logon_utc, c.department_name,
               h.state, h.dns_ip, h.winrm_available, h.checked_utc, h.error,
               ou.name, c.ip_ad
        FROM computers c
        LEFT JOIN computer_health h ON h.computer_dn = c.distinguished_name
        LEFT JOIN organizational_units ou ON ou.distinguished_name = c.ou_dn
        """;

    private static ComputerRow ReadComputerRow(Microsoft.Data.Sqlite.SqliteDataReader reader) => new()
    {
        DistinguishedName = reader.GetString(0),
        Name = reader.GetString(1),
        OuDistinguishedName = reader.GetString(2),
        OperatingSystem = reader.IsDBNull(3) ? null : reader.GetString(3),
        OperatingSystemVersion = reader.IsDBNull(4) ? null : reader.GetString(4),
        Enabled = reader.GetInt32(5) == 1,
        LastLogonUtc = reader.IsDBNull(6) ? null : ParseDateTimeOffset(reader.GetString(6)),
        DepartmentName = reader.IsDBNull(7) ? null : reader.GetString(7),
        AvailabilityState = reader.IsDBNull(8) ? AvailabilityState.Unknown : ParseAvailabilityState(reader.GetString(8)),
        DnsIpAddress = reader.IsDBNull(9) ? null : reader.GetString(9),
        WinRmAvailable = !reader.IsDBNull(10) && reader.GetInt32(10) == 1,
        LastCheckedUtc = reader.IsDBNull(11) ? null : ParseDateTimeOffset(reader.GetString(11)),
        HealthError = reader.IsDBNull(12) ? null : reader.GetString(12),
        OuName = reader.IsDBNull(13) ? null : reader.GetString(13),
        AdIpAddress = reader.IsDBNull(14) ? null : reader.GetString(14),
    };

    public async Task<IReadOnlyList<ComputerRow>> GetComputersAsync(
        string? departmentName = null,
        string? searchText = null,
        string? stateFilter = null)
    {
        using var connection = _database.CreateConnection();
        var sql = ComputerSelectColumns + " WHERE 1 = 1";

        var conditions = new List<string>();
        if (!string.IsNullOrWhiteSpace(departmentName))
        {
            conditions.Add("c.department_name = @department");
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            conditions.Add("(c.name LIKE @search OR c.distinguished_name LIKE @search)");
        }

        if (!string.IsNullOrWhiteSpace(stateFilter))
        {
            conditions.Add("h.state = @state");
        }

        sql += string.Join(" AND ", conditions);
        sql += " ORDER BY c.name COLLATE NOCASE;";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (!string.IsNullOrWhiteSpace(departmentName))
        {
            command.Parameters.AddWithValue("@department", departmentName);
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            command.Parameters.AddWithValue("@search", "%" + searchText + "%");
        }

        if (!string.IsNullOrWhiteSpace(stateFilter))
        {
            command.Parameters.AddWithValue("@state", stateFilter);
        }

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var computers = new List<ComputerRow>();
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            computers.Add(ReadComputerRow(reader));
        }

        return computers;
    }

    public async Task<IReadOnlyList<GpoLinkInfo>> GetLinksForComputerAsync(string computerDistinguishedName)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        // Liens directs : ceux de l'OU de l'ordinateur + ceux du domaine.
        // L'héritage des OU parentes n'est pas calculé ici : il est indiqué comme estimé.
        command.CommandText =
            """
            SELECT l.gpo_id, l.target_dn, l.target_type, l.[order], l.enabled, g.display_name
            FROM gpo_links l
            JOIN gpos g ON g.gpo_id = l.gpo_id
            WHERE l.target_dn = @computerOu OR l.target_type = 'Domain'
            ORDER BY l.[order];
            """;

        var computer = await GetComputerAsync(connection, computerDistinguishedName);
        var computerOu = computer?.OuDistinguishedName ?? string.Empty;
        command.Parameters.AddWithValue("@computerOu", computerOu);

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var links = new List<GpoLinkInfo>();
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            links.Add(new GpoLinkInfo
            {
                GpoId = Guid.Parse(reader.GetString(0)),
                TargetDistinguishedName = reader.GetString(1),
                TargetType = reader.GetString(2) == "Domain" ? GpoLinkTargetType.Domain : GpoLinkTargetType.OrganizationalUnit,
                Order = reader.GetInt32(3),
                IsEnabled = reader.GetInt32(4) == 1,
                GpoName = reader.IsDBNull(5) ? null : reader.GetString(5),
            });
        }

        return links;
    }

    public async Task<ComputerRow?> GetComputerAsync(string computerDistinguishedName)
    {
        using var connection = _database.CreateConnection();
        return await GetComputerAsync(connection, computerDistinguishedName);
    }

    private static async Task<ComputerRow?> GetComputerAsync(SqliteConnection connection, string computerDistinguishedName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = ComputerSelectColumns + " WHERE c.distinguished_name = @dn;";
        command.Parameters.AddWithValue("@dn", computerDistinguishedName);

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        if (!await reader.ReadAsync().ConfigureAwait(false))
        {
            return null;
        }

        return ReadComputerRow(reader);
    }

    public async Task SaveRsopReportAsync(ComputerGpoResult result, string computerDistinguishedName)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO rsop_reports (computer_dn, report_xml, applied_gpo_names, report_date_utc, source)
            VALUES (@dn, @xml, @names, @date, @source)
            ON CONFLICT(computer_dn) DO UPDATE SET
                report_xml = @xml, applied_gpo_names = @names, report_date_utc = @date, source = @source;
            """;
        command.Parameters.AddWithValue("@dn", computerDistinguishedName);
        command.Parameters.AddWithValue("@xml", result.RawReport ?? string.Empty);
        command.Parameters.AddWithValue("@names", string.Join(" | ", result.AppliedGpoNames));
        command.Parameters.AddWithValue("@date", result.ReportDateUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@source", result.ReportSource);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task<ComputerGpoResult?> GetRsopReportAsync(string computerDistinguishedName)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT report_xml, applied_gpo_names, report_date_utc, source
            FROM rsop_reports
            WHERE computer_dn = @dn;
            """;
        command.Parameters.AddWithValue("@dn", computerDistinguishedName);

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        if (!await reader.ReadAsync().ConfigureAwait(false))
        {
            return null;
        }

        return new ComputerGpoResult
        {
            ComputerName = string.Empty,
            ReportDateUtc = DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            ReportSource = reader.GetString(3),
            AppliedGpoNames = reader.IsDBNull(1) || string.IsNullOrEmpty(reader.GetString(1))
                ? Array.Empty<string>()
                : reader.GetString(1).Split(" | ", StringSplitOptions.RemoveEmptyEntries),
            RawReport = reader.GetString(0),
        };
    }

    public async Task<IReadOnlyList<GpoLinkInfo>> GetAllLinksAsync()
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT l.gpo_id, l.target_dn, l.target_type, l.[order], l.enabled, g.display_name
            FROM gpo_links l
            JOIN gpos g ON g.gpo_id = l.gpo_id
            ORDER BY g.display_name COLLATE NOCASE, l.[order];
            """;

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var links = new List<GpoLinkInfo>();
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            links.Add(new GpoLinkInfo
            {
                GpoId = Guid.Parse(reader.GetString(0)),
                TargetDistinguishedName = reader.GetString(1),
                TargetType = reader.GetString(2) == "Domain" ? GpoLinkTargetType.Domain : GpoLinkTargetType.OrganizationalUnit,
                Order = reader.GetInt32(3),
                IsEnabled = reader.GetInt32(4) == 1,
                GpoName = reader.IsDBNull(5) ? null : reader.GetString(5),
            });
        }

        return links;
    }

    /// <summary>
    /// Comptes clients lus dans le cache local (résultat de la dernière
    /// synchronisation). Le cache n'est jamais présenté comme la source de
    /// vérité : chaque ligne porte son horodatage d'observation.
    /// </summary>
    public async Task<IReadOnlyList<ClientAccountRow>> GetClientAccountsAsync(
        string? searchText = null,
        string? department = null,
        bool onlyStandardAccounts = false)
    {
        using var connection = _database.CreateConnection();
        var sql =
            """
            SELECT a.distinguished_name, a.sam_account_name, a.display_name, a.given_name,
                   a.surname, a.email, a.title, a.department, a.ou_dn, a.enabled,
                   a.is_administrator, a.account_locked, a.password_never_expires,
                   a.last_logon_utc, a.last_seen_utc, ou.name
            FROM client_accounts a
            LEFT JOIN organizational_units ou ON ou.distinguished_name = a.ou_dn
            WHERE 1 = 1
            """;

        var conditions = new List<string>();
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            conditions.Add("(a.sam_account_name LIKE @search OR a.display_name LIKE @search OR a.email LIKE @search)");
        }

        if (!string.IsNullOrWhiteSpace(department))
        {
            conditions.Add("a.department = @department");
        }

        if (onlyStandardAccounts)
        {
            conditions.Add("a.is_administrator = 0");
        }

        sql += string.Join(" AND ", conditions);
        sql += " ORDER BY a.sam_account_name COLLATE NOCASE;";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            command.Parameters.AddWithValue("@search", "%" + searchText + "%");
        }

        if (!string.IsNullOrWhiteSpace(department))
        {
            command.Parameters.AddWithValue("@department", department);
        }

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var accounts = new List<ClientAccountRow>();
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            accounts.Add(new ClientAccountRow
            {
                DistinguishedName = reader.GetString(0),
                SamAccountName = reader.GetString(1),
                DisplayName = reader.IsDBNull(2) ? null : reader.GetString(2),
                GivenName = reader.IsDBNull(3) ? null : reader.GetString(3),
                Surname = reader.IsDBNull(4) ? null : reader.GetString(4),
                Email = reader.IsDBNull(5) ? null : reader.GetString(5),
                Title = reader.IsDBNull(6) ? null : reader.GetString(6),
                Department = reader.IsDBNull(7) ? null : reader.GetString(7),
                OuDistinguishedName = reader.IsDBNull(8) ? null : reader.GetString(8),
                OuName = reader.IsDBNull(15) ? null : reader.GetString(15),
                Enabled = reader.GetInt32(9) == 1,
                IsAdministrator = reader.GetInt32(10) == 1,
                AccountLocked = reader.GetInt32(11) == 1,
                PasswordNeverExpires = reader.GetInt32(12) == 1,
                LastLogonUtc = reader.IsDBNull(13) ? null : ParseDateTimeOffset(reader.GetString(13)),
                LastSeenUtc = ParseDateTimeOffset(reader.GetString(14)),
            });
        }

        return accounts;
    }

    /// <summary>Départements présents dans le cache des comptes clients.</summary>
    public async Task<IReadOnlyList<string>> GetClientAccountDepartmentsAsync()
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT DISTINCT department FROM client_accounts
            WHERE department IS NOT NULL AND department <> ''
            ORDER BY department COLLATE NOCASE;
            """;

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var departments = new List<string>();
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            departments.Add(reader.GetString(0));
        }

        return departments;
    }

    private static async Task<int> ScalarIntAsync(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync().ConfigureAwait(false) ?? 0, CultureInfo.InvariantCulture);
    }

    private static async Task<string?> ScalarTextAsync(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync().ConfigureAwait(false);
        return value is null or DBNull ? null : value.ToString();
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result)
            ? result
            : null;

    private static AvailabilityState ParseAvailabilityState(string value) => value switch
    {
        "Online" => AvailabilityState.Online,
        "OnlineWithoutWinRm" => AvailabilityState.OnlineWithoutWinRm,
        "DnsUnresolved" => AvailabilityState.DnsUnresolved,
        _ => AvailabilityState.Unknown,
    };
}

/// <summary>Instantané du tableau de bord — aucune donnée n'est inventée.</summary>
public sealed class DashboardSnapshot
{
    public int TotalComputers { get; init; }
    public int OnlineComputers { get; init; }
    public int UnreachableComputers { get; init; }
    public int UnknownComputers { get; init; }
    public int OrganizationalUnits { get; init; }
    public int Gpos { get; init; }
    public int GpoLinks { get; init; }
    public int ClientAccounts { get; init; }
    public int AdministratorAccounts { get; init; }
    public int Departments { get; init; }
    public int UnmappedComputers { get; init; }
    public DateTimeOffset? LastSyncUtc { get; init; }
    public DateTimeOffset? LastBackupUtc { get; init; }
}

/// <summary>Ligne d'ordinateur affichée dans le parc.</summary>
public sealed class ComputerRow
{
    public string DistinguishedName { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string OuDistinguishedName { get; init; } = string.Empty;
    public string? OuName { get; init; }
    public string? OperatingSystem { get; init; }
    public string? OperatingSystemVersion { get; init; }
    public bool Enabled { get; init; }
    public DateTimeOffset? LastLogonUtc { get; init; }
    public string? DepartmentName { get; init; }
    public AvailabilityState AvailabilityState { get; init; }
    public string? DnsIpAddress { get; init; }
    public bool WinRmAvailable { get; init; }
    public DateTimeOffset? LastCheckedUtc { get; init; }
    public string? HealthError { get; init; }

    /// <summary>
    /// Adresse IPv4 déclarée dans l'objet ordinateur AD DS (attribut IPv4Address),
    /// last-known-good : conservée quand une résolution DNS échoue.
    /// </summary>
    public string? AdIpAddress { get; init; }

    /// <summary>Adresse IP la plus fiable disponible : DNS vérifié, sinon déclaratif AD DS.</summary>
    public string? EffectiveIpAddress =>
        !string.IsNullOrWhiteSpace(DnsIpAddress) ? DnsIpAddress
        : !string.IsNullOrWhiteSpace(AdIpAddress) ? AdIpAddress
        : null;

    public string AvailabilityText => AvailabilityState switch
    {
        AvailabilityState.Online => "En ligne (WinRM)",
        AvailabilityState.OnlineWithoutWinRm => "Joignable (DNS), WinRM indisponible",
        AvailabilityState.DnsUnresolved => "DNS non résolu",
        _ => "Non vérifié",
    };

    public string AvailabilityMethod => AvailabilityState switch
    {
        AvailabilityState.Online or AvailabilityState.OnlineWithoutWinRm => "DNS + WinRM (Test-WSMan)",
        AvailabilityState.DnsUnresolved => "DNS (Resolve-DnsName)",
        _ => "Aucun test",
    };
}

/// <summary>OU candidate au mapping département, avec son affectation actuelle.</summary>
public sealed class OuMappingRow
{
    public string DistinguishedName { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int ComputerCount { get; init; }
    public string? DepartmentName { get; init; }

    public string MappingText => string.IsNullOrWhiteSpace(DepartmentName)
        ? "Non classé"
        : DepartmentName;
}

/// <summary>Compte client lu dans le cache local (résultat de la synchronisation).</summary>
public sealed class ClientAccountRow
{
    public string DistinguishedName { get; init; } = string.Empty;
    public string SamAccountName { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string? GivenName { get; init; }
    public string? Surname { get; init; }
    public string? Email { get; init; }
    public string? Title { get; init; }
    public string? Department { get; init; }
    public string? OuDistinguishedName { get; init; }
    public string? OuName { get; init; }
    public bool Enabled { get; init; }
    public bool IsAdministrator { get; init; }
    public bool AccountLocked { get; init; }
    public bool PasswordNeverExpires { get; init; }
    public DateTimeOffset? LastLogonUtc { get; init; }
    public DateTimeOffset? LastSeenUtc { get; init; }

    /// <summary>Libellé affiché : nom complet si connu, sinon le nom de connexion.</summary>
    public string DisplayText =>
        string.IsNullOrWhiteSpace(DisplayName) ? SamAccountName : DisplayName;

    /// <summary>Statut global du compte (désactivé, verrouillé, admin, standard).</summary>
    public string StatusText
    {
        get
        {
            if (!Enabled)
            {
                return "Désactivé";
            }

            if (AccountLocked)
            {
                return "Verrouillé";
            }

            return IsAdministrator ? "Administrateur" : "Standard";
        }
    }
}
