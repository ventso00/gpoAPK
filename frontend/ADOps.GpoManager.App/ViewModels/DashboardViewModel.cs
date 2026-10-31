using System.Collections.ObjectModel;
using ADOps.GpoManager.App.Services;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Services;
using ADOps.GpoManager.Infrastructure.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ADOps.GpoManager.App.ViewModels;

/// <summary>
/// Tableau de bord : état de la session, dernier instantané synchronisé du
/// domaine (cache local horodaté), découverte des machines du masque réseau du
/// serveur et dernières opérations auditées.
/// </summary>
public sealed partial class DashboardViewModel : ViewModelBase
{
    private readonly IAuditService _audit;
    private readonly InventoryQueryRepository _inventory;
    private readonly INetworkDiscoveryService _discovery;
    private readonly IInventorySyncService _sync;
    private readonly ILogger _logger;

    [ObservableProperty]
    private string _connectionStatus = "Non connecté";

    [ObservableProperty]
    private string _gpoCountText = "—";

    [ObservableProperty]
    private string _auditCountText = "—";

    [ObservableProperty]
    private string _computersSummaryText = "—";

    [ObservableProperty]
    private string _ouCountText = "—";

    [ObservableProperty]
    private string _accountsSummaryText = "—";

    [ObservableProperty]
    private string _sessionText = "Aucune session de domaine";

    [ObservableProperty]
    private string _healthText = string.Empty;

    [ObservableProperty]
    private string _syncDetailText = "Aucune synchronisation";

    [ObservableProperty]
    private string _syncWarningsText = string.Empty;

    [ObservableProperty]
    private string _subnetSummaryText = "Aucune découverte";

    [ObservableProperty]
    private bool _showHosts;

    [ObservableProperty]
    private string _subnetStatusMessage = string.Empty;

    [ObservableProperty]
    private bool _showSubnetStatus;

    [ObservableProperty]
    private bool _isSubnetStatusError;

    public ObservableCollection<NetworkHostInfo> Hosts { get; } = new();

    public ObservableCollection<AuditEntryViewModel> RecentEntries { get; } = new();

    public ConnectionState Connection { get; }

    public DashboardViewModel(
        IAuditService audit,
        ConnectionState connection,
        InventoryQueryRepository inventory,
        INetworkDiscoveryService discovery,
        IInventorySyncService sync,
        ILogger logger)
    {
        _audit = audit;
        Connection = connection;
        _inventory = inventory;
        _discovery = discovery;
        _sync = sync;
        _logger = logger;
    }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        _ = RefreshAsync();
    }

    /// <summary>
    /// Recharge le tableau de bord : état de la session, instantané du parc
    /// (cache local horodaté) et dernières opérations auditées. Appelé à
    /// l'arrivée sur la page et après chaque synchronisation.
    /// </summary>
    public async Task RefreshAsync()
    {
        RefreshDashboard();
        await LoadSnapshotAsync().ConfigureAwait(true);
        await LoadRecentAuditAsync().ConfigureAwait(true);
    }

    private void RefreshDashboard()
    {
        ConnectionStatus = Connection.IsConnected && Connection.Current is not null
            ? "Connecté à " + Connection.Current.DomainFqdn + " via " + Connection.Current.ServerFqdn
            : "Non connecté";

        GpoCountText = Connection.LastGpoCount?.ToString() ?? "—";

        var context = Connection.Current;
        if (context is null)
        {
            SessionText = "Aucune session de domaine — lancez la synchronisation.";
            HealthText = Connection.HealthSummary ?? string.Empty;
            return;
        }

        var address = string.IsNullOrWhiteSpace(Connection.ServerAddress)
            ? string.Empty
            : " (" + Connection.ServerAddress + ")";
        var netbios = string.IsNullOrWhiteSpace(context.DomainNetbios)
            ? string.Empty
            : " (NetBIOS " + context.DomainNetbios + ")";

        SessionText = context.DomainFqdn + netbios + " via " + context.ServerFqdn + address
            + " — mode " + (context.ExecutorMode == ExecutorMode.Local ? "local (RSAT)" : "distant (WinRM)")
            + " — compte " + context.Credentials.ToDisplayString();

        HealthText = Connection.HealthSummary ?? string.Empty;

        var duration = Connection.LastSyncDuration is null
            ? string.Empty
            : " (" + Connection.LastSyncDuration.Value.TotalSeconds.ToString("0.0") + " s)";

        SyncDetailText = Connection.LastSyncUtc is null
            ? "Aucune synchronisation"
            : "Dernière synchronisation : " + Connection.LastSyncUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
              + " — " + (Connection.LastSyncSummary ?? string.Empty) + duration;

        SyncWarningsText = Connection.LastSyncWarnings.Count == 0
            ? string.Empty
            : string.Join("  |  ", Connection.LastSyncWarnings);
    }

    /// <summary>Instantané du parc depuis le cache local (horodaté, jamais inventé).</summary>
    private async Task LoadSnapshotAsync()
    {
        try
        {
            // Le cache est peuplé par une synchronisation : s'il est vide et
            // qu'aucune synchronisation n'a encore eu lieu, on la lance une
            // seule fois (une tentative échouée n'est pas réessayée à chaque
            // navigation). Si une synchronisation est déjà en cours — cas de la
            // synchronisation de démarrage — on ne la double pas.
            if (Connection.IsConnected
                && Connection.Current is not null
                && Connection.LastSyncState == SyncState.NotRun
                && !Connection.IsSyncInProgress)
            {
                var probe = await _inventory.GetComputersAsync();
                if (probe.Count == 0)
                {
                    Connection.IsSyncInProgress = true;
                    try
                    {
                        var summary = await _sync.SyncAsync(Connection.Current);
                        Connection.SetSyncResult(summary);
                        Connection.LastGpoCount = summary.GposRead;
                    }
                    finally
                    {
                        Connection.IsSyncInProgress = false;
                    }
                }
            }

            var snapshot = await _inventory.GetDashboardSnapshotAsync();
            ComputersSummaryText = snapshot.TotalComputers + " ordinateurs ("
                + snapshot.OnlineComputers + " en ligne, "
                + snapshot.UnreachableComputers + " hors ligne, "
                + snapshot.UnknownComputers + " non testés)";
            OuCountText = snapshot.OrganizationalUnits + " OU · "
                + snapshot.Departments + " département(s) · "
                + snapshot.UnmappedComputers + " non classé(s)";
            AccountsSummaryText = snapshot.ClientAccounts + " comptes clients ("
                + snapshot.AdministratorAccounts + " administrateur(s)) · "
                + snapshot.GpoLinks + " lien(s) de GPO";
        }
        catch (Exception ex)
        {
            _logger.Error("Chargement de l'instantané du tableau de bord échoué", ex);
            ComputersSummaryText = "Erreur de chargement";
        }
    }

    private async Task LoadRecentAuditAsync()
    {
        try
        {
            var count = await _audit.GetCountAsync();
            AuditCountText = count.ToString();

            var entries = await _audit.GetRecentAsync(5);
            RecentEntries.Clear();
            foreach (var entry in entries)
            {
                RecentEntries.Add(new AuditEntryViewModel(entry));
            }
        }
        catch
        {
            AuditCountText = "Erreur";
        }
    }

    /// <summary>
    /// Synchronisation manuelle depuis le tableau de bord : même moteur que le
    /// bouton global, mais avec un retour détaillé dans la page.
    /// </summary>
    [RelayCommand]
    private async Task SynchronizeAsync()
    {
        if (Connection.Current is null || Connection.IsSyncInProgress)
        {
            return;
        }

        IsBusy = true;
        Connection.IsSyncInProgress = true;
        try
        {
            var summary = await _sync.SyncAsync(Connection.Current);
            Connection.SetSyncResult(summary);
            Connection.LastGpoCount = summary.GposRead;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.Error("Synchronisation depuis le tableau de bord échouée", ex);
            Connection.MarkSyncFailed(ex.Message);
            RefreshDashboard();
        }
        finally
        {
            Connection.IsSyncInProgress = false;
            IsBusy = false;
        }
    }

    /// <summary>
    /// Découverte des machines du masque réseau du serveur cible
    /// (balayage ICMP croisé avec l'inventaire AD DS).
    /// </summary>
    [RelayCommand]
    private async Task DiscoverSubnetAsync()
    {
        if (!Connection.IsConnected || Connection.Current is null)
        {
            SetSubnetStatus(true, "Aucun domaine connecté : la découverte du masque réseau nécessite une connexion.");
            return;
        }

        IsBusy = true;
        SetSubnetStatus(false, string.Empty);

        try
        {
            // Inventaire AD DS frais pour le croisement (ordinateurs connus).
            var computers = await _inventory.GetComputersAsync();
            var knownComputers = computers
                .Select(row => new ComputerInfo
                {
                    Name = row.Name,
                    DistinguishedName = row.DistinguishedName,
                    SamAccountName = row.Name + "$",
                    OuDistinguishedName = row.OuDistinguishedName,
                    OperatingSystem = row.OperatingSystem,
                    OperatingSystemVersion = row.OperatingSystemVersion,
                    Enabled = row.Enabled,
                    LastLogonUtc = row.LastLogonUtc,
                })
                .ToList();

            var result = await _discovery.DiscoverAsync(Connection.Current, knownComputers);

            if (result.IsSuccess && result.Value is not null)
            {
                Hosts.Clear();
                foreach (var host in result.Value)
                {
                    Hosts.Add(host);
                }

                ShowHosts = true;
                var online = result.Value.Count(host => host.Online);
                var adKnown = result.Value.Count(host => host.IsAdComputer);
                SubnetSummaryText = online + " machine(s) connectée(s) sur le masque réseau · "
                    + adKnown + " connue(s) d'AD DS · "
                    + result.Value.Count + " hôte(s) listé(s)";
                SetSubnetStatus(false, "Découverte terminée : " + SubnetSummaryText
                    + ". Un ping échoué ne prouve pas qu'une machine est éteinte (ICMP est souvent bloqué).");
            }
            else if (result.Error is not null)
            {
                SetSubnetStatus(true, result.Error.ToDisplayString());
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Découverte du sous-réseau échouée", ex);
            SetSubnetStatus(true, "Erreur inattendue : " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetSubnetStatus(bool isError, string message)
    {
        IsSubnetStatusError = isError;
        SubnetStatusMessage = message;
        ShowSubnetStatus = !string.IsNullOrWhiteSpace(message);
    }
}
