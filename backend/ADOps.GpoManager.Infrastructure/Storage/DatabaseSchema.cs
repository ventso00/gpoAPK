using Microsoft.Data.Sqlite;

namespace ADOps.GpoManager.Infrastructure.Storage;

/// <summary>
/// Schéma SQLite local (version 1) : profils de domaine, runs de synchronisation,
/// snapshot OU/ordinateurs/GPO/liens, santé des postes, rapports RSoP, sauvegardes,
/// mapping départements et audit enrichi. AD DS reste la source de vérité :
/// cette base est un cache local avec horodatage de dernière observation.
/// </summary>
internal static class DatabaseSchema
{
    private static readonly string[] Tables =
    {
        """
        CREATE TABLE IF NOT EXISTS domain_profiles (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            server_fqdn TEXT NOT NULL,
            domain_fqdn TEXT NOT NULL,
            domain_netbios TEXT,
            executor_mode TEXT NOT NULL,
            last_connected_utc TEXT,
            is_current INTEGER NOT NULL DEFAULT 0
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS sync_runs (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            started_utc TEXT NOT NULL,
            finished_utc TEXT,
            state TEXT NOT NULL,
            ous_read INTEGER NOT NULL DEFAULT 0,
            computers_read INTEGER NOT NULL DEFAULT 0,
            computers_added INTEGER NOT NULL DEFAULT 0,
            computers_updated INTEGER NOT NULL DEFAULT 0,
            computers_removed INTEGER NOT NULL DEFAULT 0,
            gpos_read INTEGER NOT NULL DEFAULT 0,
            links_read INTEGER NOT NULL DEFAULT 0,
            warnings TEXT,
            error TEXT
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS organizational_units (
            distinguished_name TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            canonical_name TEXT,
            parent_dn TEXT,
            computer_count INTEGER NOT NULL DEFAULT 0,
            last_seen_utc TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS computers (
            distinguished_name TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            sam_account_name TEXT,
            ou_dn TEXT NOT NULL,
            operating_system TEXT,
            operating_system_version TEXT,
            enabled INTEGER NOT NULL DEFAULT 1,
            last_logon_utc TEXT,
            department_name TEXT,
            ip_ad TEXT,
            last_seen_utc TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS client_accounts (
            distinguished_name TEXT PRIMARY KEY,
            sam_account_name TEXT NOT NULL,
            display_name TEXT,
            given_name TEXT,
            surname TEXT,
            email TEXT,
            title TEXT,
            department TEXT,
            ou_dn TEXT,
            enabled INTEGER NOT NULL DEFAULT 1,
            is_administrator INTEGER NOT NULL DEFAULT 0,
            account_locked INTEGER NOT NULL DEFAULT 0,
            password_never_expires INTEGER NOT NULL DEFAULT 0,
            last_logon_utc TEXT,
            last_seen_utc TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS assistant_targets (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            kind TEXT NOT NULL,
            distinguished_name TEXT NOT NULL,
            display_name TEXT,
            sam_account_name TEXT,
            ou_dn TEXT,
            is_selected INTEGER NOT NULL DEFAULT 1,
            note TEXT,
            last_seen_utc TEXT NOT NULL,
            UNIQUE (kind, distinguished_name)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS department_mappings (
            department_name TEXT PRIMARY KEY,
            ou_dn TEXT,
            updated_utc TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS gpos (
            gpo_id TEXT PRIMARY KEY,
            display_name TEXT NOT NULL,
            description TEXT,
            gpo_status TEXT NOT NULL,
            creation_time_utc TEXT,
            modification_time_utc TEXT,
            domain_name TEXT,
            last_seen_utc TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS gpo_links (
            gpo_id TEXT NOT NULL,
            target_dn TEXT NOT NULL,
            target_type TEXT NOT NULL,
            [order] INTEGER NOT NULL,
            enabled INTEGER NOT NULL DEFAULT 1,
            last_seen_utc TEXT NOT NULL,
            PRIMARY KEY (gpo_id, target_dn)
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS computer_health (
            computer_dn TEXT PRIMARY KEY,
            state TEXT NOT NULL,
            method TEXT NOT NULL,
            dns_resolved INTEGER NOT NULL DEFAULT 0,
            dns_ip TEXT,
            winrm_available INTEGER NOT NULL DEFAULT 0,
            checked_utc TEXT NOT NULL,
            error TEXT
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS rsop_reports (
            computer_dn TEXT PRIMARY KEY,
            report_xml TEXT NOT NULL,
            applied_gpo_names TEXT NOT NULL,
            report_date_utc TEXT NOT NULL,
            source TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS backup_records (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            backup_id TEXT NOT NULL,
            gpo_id TEXT NOT NULL,
            gpo_name TEXT NOT NULL,
            created_utc TEXT NOT NULL,
            location TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS gpo_deployments (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            gpo_id TEXT NOT NULL,
            gpo_name TEXT NOT NULL,
            template_id TEXT NOT NULL,
            computer_name TEXT NOT NULL,
            computer_dn TEXT NOT NULL,
            ou_dn TEXT NOT NULL DEFAULT '',
            filter_group TEXT,
            settings_json TEXT NOT NULL DEFAULT '[]',
            deployed_utc TEXT NOT NULL,
            UNIQUE (gpo_id, computer_dn)
        )
        """,
    };

    /// <summary>Colonnes ajoutées à l'audit existant (migration depuis le prototype Phase 1).</summary>
    private static readonly string[] AuditColumns =
    {
        "operation_id TEXT",
        "server TEXT",
        "domain TEXT",
        "object_guid TEXT",
        "targets TEXT",
        "initial_state TEXT",
        "requested_state TEXT",
        "error_code TEXT",
        "duration_ms INTEGER",
    };

    /// <summary>
    /// Colonnes ajoutées à <c>sync_runs</c> après la première version du schéma
    /// (les comptes utilisateurs sont désormais collectés par la synchronisation).
    /// </summary>
    private static readonly string[] SyncRunColumns =
    {
        "users_read INTEGER NOT NULL DEFAULT 0",
    };

    /// <summary>
    /// Adresse IPv4 déclarée dans l'objet ordinateur AD DS (attribut IPv4Address) :
    /// last-known-good affiché quand la résolution DNS live échoue.
    /// </summary>
    private static readonly string[] ComputerColumns =
    {
        "ip_ad TEXT",
    };

    public static void Initialize(SqliteDatabase database)
    {
        using var connection = database.CreateConnection();
        using var transaction = connection.BeginTransaction();

        foreach (var table in Tables)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = table;
            command.ExecuteNonQuery();
        }

        MigrateAuditTable(connection, transaction);
        EnsureColumns(connection, transaction, "sync_runs", SyncRunColumns);
        EnsureColumns(connection, transaction, "computers", ComputerColumns);
        transaction.Commit();
    }

    /// <summary>
    /// Ajoute les colonnes manquantes d'une table existante. Indispensable :
    /// les tables sont créées avec « IF NOT EXISTS », donc une base déjà
    /// présente ne reçoit pas les colonnes ajoutées ultérieurement.
    /// </summary>
    private static void EnsureColumns(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        IReadOnlyList<string> columnDefinitions)
    {
        if (columnDefinitions.Count == 0)
        {
            return;
        }

        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var pragma = connection.CreateCommand())
        {
            pragma.Transaction = transaction;
            pragma.CommandText = "PRAGMA table_info(" + table + ");";
            using var reader = pragma.ExecuteReader();
            while (reader.Read())
            {
                existingColumns.Add(reader.GetString(1));
            }
        }

        foreach (var columnDefinition in columnDefinitions)
        {
            var columnName = columnDefinition.Split(' ')[0];
            if (existingColumns.Contains(columnName))
            {
                continue;
            }

            using var alter = connection.CreateCommand();
            alter.Transaction = transaction;
            alter.CommandText = "ALTER TABLE " + table + " ADD COLUMN " + columnDefinition + ";";
            alter.ExecuteNonQuery();
        }
    }

    private static void MigrateAuditTable(SqliteConnection connection, SqliteTransaction transaction)
    {
        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var pragma = connection.CreateCommand())
        {
            pragma.Transaction = transaction;
            pragma.CommandText = "PRAGMA table_info(audit_entries);";
            using var reader = pragma.ExecuteReader();
            while (reader.Read())
            {
                existingColumns.Add(reader.GetString(1));
            }
        }

        foreach (var columnDefinition in AuditColumns)
        {
            var columnName = columnDefinition.Split(' ')[0];
            if (existingColumns.Contains(columnName))
            {
                continue;
            }

            using var alter = connection.CreateCommand();
            alter.Transaction = transaction;
            alter.CommandText = "ALTER TABLE audit_entries ADD COLUMN " + columnDefinition + ";";
            alter.ExecuteNonQuery();
        }
    }
}
