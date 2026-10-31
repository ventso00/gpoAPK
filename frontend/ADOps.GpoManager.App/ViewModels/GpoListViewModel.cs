using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using ADOps.GpoManager.App.Services;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Services;
using ADOps.GpoManager.Infrastructure.Storage;
using ADOps.GpoManager.PowerShell.Host;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ADOps.GpoManager.App.ViewModels;

public sealed partial class GpoListViewModel : ViewModelBase
{
    private readonly IGpoService _gpoService;
    private readonly IGpoDeploymentService _deployment;
    private readonly IDirectoryService _directory;
    private readonly InventoryQueryRepository _inventory;
    private readonly IInventorySyncService _sync;
    private readonly ILogger _logger;
    private readonly ConnectionState _connection;
    private readonly IGpoScriptCatalog _scriptCatalog;
    private readonly IPowerShellExecutorFactory _executorFactory;
    private readonly ICollectionView _gposView;
    private bool _autoLoaded;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _showStatus;

    [ObservableProperty]
    private bool _isStatusError;

    private string _filter = string.Empty;

    [ObservableProperty]
    private GpoSummary? _selectedGpo;

    public ObservableCollection<GpoSummary> Gpos { get; } = new();

    public ICollectionView GposView => _gposView;

    public ConnectionState Connection => _connection;

    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value))
            {
                _gposView.Refresh();
            }
        }
    }

    public GpoListViewModel(
        IGpoService gpoService,
        IGpoDeploymentService deployment,
        IDirectoryService directory,
        InventoryQueryRepository inventory,
        IInventorySyncService sync,
        ILogger logger,
        IGpoScriptCatalog scriptCatalog,
        IPowerShellExecutorFactory executorFactory,
        ConnectionState connection)
    {
        _gpoService = gpoService;
        _deployment = deployment;
        _directory = directory;
        _inventory = inventory;
        _sync = sync;
        _logger = logger;
        _scriptCatalog = scriptCatalog;
        _executorFactory = executorFactory;
        _connection = connection;
        _gposView = CollectionViewSource.GetDefaultView(Gpos);
        _gposView.Filter = FilterGpo;
    }

    [ObservableProperty]
    private bool _isApplying;

    [RelayCommand]
    private async Task QuickApplyAsync(GpoSummary? gpo)
    {
        if (gpo is null) { return; }
        if (!_connection.IsConnected || _connection.Current is null)
        {
            SetStatus(true, "Aucun domaine connecté : impossible d'appliquer la GPO.");
            return;
        }

        IsApplying = true;
        SetStatus(false, "Application de « " + gpo.DisplayName + " » en cours…");

        try
        {
            var context = _connection.Current;

            // OUs : synchronisation si le cache local est vide
            var ouRows = await _inventory.GetOuMappingsAsync();
            if (ouRows.Count == 0)
            {
                var summary = await _sync.SyncAsync(context);
                _connection.SetSyncResult(summary);
                ouRows = await _inventory.GetOuMappingsAsync();
            }

            var targetOus = ouRows
                .Select(row => new OuInfo { DistinguishedName = row.DistinguishedName, Name = row.Name })
                .ToList();

            if (targetOus.Count == 0)
            {
                SetStatus(true, "Aucune unité d'organisation cible disponible.");
                return;
            }

            var usersResult = await _directory.ListUsersAsync(context);
            var users = usersResult.IsSuccess && usersResult.Value is not null
                ? usersResult.Value
                : Array.Empty<UserInfo>();

            var computerRows = await _inventory.GetComputersAsync();
            var computers = computerRows.Select(row => new ComputerInfo
            {
                Name = row.Name,
                DistinguishedName = row.DistinguishedName,
                SamAccountName = row.Name + "$",
                OuDistinguishedName = row.OuDistinguishedName,
                OperatingSystem = row.OperatingSystem,
                OperatingSystemVersion = row.OperatingSystemVersion,
                Enabled = row.Enabled,
                LastLogonUtc = row.LastLogonUtc,
            }).ToList();

            var cachedLinks = await _inventory.GetAllLinksAsync();

            var planResult = await _deployment.PlanDeploymentAsync(
                context, new[] { gpo }, targetOus, users, computers, cachedLinks);

            if (!planResult.IsSuccess || planResult.Value is null)
            {
                SetStatus(true, planResult.Error?.ToDisplayString() ?? "Plan de déploiement invalide.");
                return;
            }

            var deployResult = await _deployment.DeployAsync(context, planResult.Value);

            if (deployResult.IsSuccess)
            {
                SetStatus(false, "GPO « " + gpo.DisplayName + " » appliquée sur " + targetOus.Count + " OU(s).");
                await _inventory.GetAllLinksAsync();
            }
            else
            {
                SetStatus(true, deployResult.Error?.ToDisplayString() ?? "Échec de l'application.");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Application directe de la GPO échouée", ex);
            SetStatus(true, "Erreur : " + ex.Message);
        }
        finally
        {
            IsApplying = false;
        }
    }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        if (!_autoLoaded && _connection.IsConnected)
        {
            _autoLoaded = true;
            _ = RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!_connection.IsConnected || _connection.Current is null)
        {
            SetStatus(false, "Aucun domaine connecté. Utilisez la page « Connexion » pour configurer et tester le serveur cible.");
            return;
        }

        IsBusy = true;
        SetStatus(false, string.Empty);

        try
        {
            var result = await _gpoService.ListGposAsync(_connection.Current);

            if (result.IsSuccess && result.Value is not null)
            {
                Gpos.Clear();
                foreach (var gpo in result.Value)
                {
                    Gpos.Add(gpo);
                }

                _connection.LastGpoCount = Gpos.Count;
                SetStatus(false, result.Value.Count == 0
                    ? "Aucune GPO dans le domaine " + _connection.Current.DomainFqdn + "."
                    : Gpos.Count + " GPO(s) chargée(s) depuis " + _connection.Current.DomainFqdn + ".");
            }
            else if (result.Error is not null)
            {
                SetStatus(true, result.Error.ToDisplayString());
            }
        }
        catch (Exception ex)
        {
            SetStatus(true, "Erreur inattendue : " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetStatus(bool isError, string message)
    {
        IsStatusError = isError;
        StatusMessage = message;
        ShowStatus = !string.IsNullOrWhiteSpace(message);
    }

    private bool FilterGpo(object item)
    {
        if (item is not GpoSummary gpo)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(Filter))
        {
            return true;
        }

        return gpo.DisplayName.Contains(Filter, StringComparison.OrdinalIgnoreCase)
            || gpo.Id.ToString().Contains(Filter, StringComparison.OrdinalIgnoreCase)
            || (gpo.Description?.Contains(Filter, StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
