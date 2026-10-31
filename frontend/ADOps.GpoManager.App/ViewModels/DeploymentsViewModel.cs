using System.Collections.ObjectModel;
using System.Windows;
using ADOps.GpoManager.App.Services;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Services;
using ADOps.GpoManager.Infrastructure.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ADOps.GpoManager.App.ViewModels;

/// <summary>
/// Module de gestion des GPO :
/// 1. sélection multiple de GPO (cases à cocher) ;
/// 2. sélection des unités d'organisation cibles ;
/// 3. rapport d'impact en temps réel (utilisateurs, ordinateurs
///    et départements/OU ciblés) dès qu'une case change ;
/// 4. validation puis déploiement des liens (New-GPLink),
///    chaque opération étant auditée ;
/// 5. suppression de liens existants (Remove-GPLink).
/// Aucune modification n'est effectuée sans l'étape de validation.
/// </summary>
public sealed partial class DeploymentsViewModel : ViewModelBase
{
    private readonly IGpoDeploymentService _deployment;
    private readonly IGpoService _gpoService;
    private readonly IDirectoryService _directory;
    private readonly InventoryQueryRepository _inventory;
    private readonly IInventorySyncService _sync;
    private readonly INavigationService _navigation;
    private readonly ILogger _logger;

    private IReadOnlyList<UserInfo> _users = Array.Empty<UserInfo>();
    private IReadOnlyList<ComputerInfo> _computers = Array.Empty<ComputerInfo>();
    private IReadOnlyList<GpoLinkInfo> _cachedLinks = Array.Empty<GpoLinkInfo>();
    private IReadOnlyList<OuMappingRow> _ouRows = Array.Empty<OuMappingRow>();
    private bool _impactUpdatePending;
    private bool _impactUpdateRequested;

    public ObservableCollection<GpoSelection> Gpos { get; } = new();
    public ObservableCollection<OuTarget> Targets { get; } = new();
    public ObservableCollection<GpoImpactEntry> ImpactEntries { get; } = new();
    public ObservableCollection<GpoLinkOperationResult> DeploymentResults { get; } = new();
    public ObservableCollection<GpoLinkInfo> SelectedOuLinks { get; } = new();

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _showStatus;

    [ObservableProperty]
    private bool _isStatusError;

    [ObservableProperty]
    private string _impactSummaryText = "Sélectionnez des GPO et des unités d'organisation pour calculer le rapport d'impact.";

    [ObservableProperty]
    private string _affectedUsersText = "0";

    [ObservableProperty]
    private string _affectedComputersText = "0";

    [ObservableProperty]
    private string _targetCountText = "0 cible(s)";

    [ObservableProperty]
    private bool _hasValidPlan;

    [ObservableProperty]
    private GpoImpactEntry? _selectedImpactEntry;

    [ObservableProperty]
    private string _targetFilter = string.Empty;

    [ObservableProperty]
    private bool _showResults;

    private GpoDeploymentPlan? _plan;

    public DeploymentsViewModel(
        IGpoDeploymentService deployment,
        IGpoService gpoService,
        IDirectoryService directory,
        InventoryQueryRepository inventory,
        IInventorySyncService sync,
        INavigationService navigation,
        ConnectionState connection,
        ILogger logger)
    {
        _deployment = deployment;
        _gpoService = gpoService;
        _directory = directory;
        _inventory = inventory;
        _sync = sync;
        _navigation = navigation;
        Connection = connection;
        _logger = logger;
    }

    public ConnectionState Connection { get; }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        _ = LoadCatalogAsync();
    }

    /// <summary>
    /// Charge le catalogue : GPO du domaine (Get-GPO -All),
    /// OUs (cache de synchronisation), liens existants,
    /// utilisateurs et ordinateurs (nécessaires au rapport d'impact).
    /// </summary>
    private async Task LoadCatalogAsync()
    {
        if (!Connection.IsConnected || Connection.Current is null)
        {
            SetStatus(true, "Aucun domaine connecté : le module de déploiement nécessite une connexion.");
            return;
        }

        var context = Connection.Current;
        IsBusy = true;
        SetStatus(false, string.Empty);

        try
        {
            // OUs : synchronisation si le cache local est vide
            _ouRows = await _inventory.GetOuMappingsAsync();
            if (_ouRows.Count == 0)
            {
                var summary = await _sync.SyncAsync(context);
                Connection.SetSyncResult(summary);
                _ouRows = await _inventory.GetOuMappingsAsync();
            }

            // Liens existants (cache local horodaté)
            _cachedLinks = await _inventory.GetAllLinksAsync();

            // GPO du domaine
            var gposResult = await _gpoService.ListGposAsync(context);
            Gpos.Clear();
            if (gposResult.IsSuccess && gposResult.Value is not null)
            {
                foreach (var gpo in gposResult.Value)
                {
                    Gpos.Add(new GpoSelection(gpo, OnSelectionChanged));
                }

                Connection.LastGpoCount = Gpos.Count;
            }

            // Cibles OU
            Targets.Clear();
            foreach (var row in _ouRows.OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase))
            {
                Targets.Add(new OuTarget(row, OnSelectionChanged));
            }

            // Utilisateurs et ordinateurs (impact)
            var usersResult = await _directory.ListUsersAsync(context);
            _users = usersResult.IsSuccess && usersResult.Value is not null
                ? usersResult.Value
                : Array.Empty<UserInfo>();

            var computerRows = await _inventory.GetComputersAsync();
            _computers = computerRows
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

            SetStatus(false, Gpos.Count + " GPO(s), " + Targets.Count + " OU(s), "
                + _users.Count + " utilisateur(s) et " + _computers.Count + " ordinateur(s) chargé(s) depuis "
                + context.DomainFqdn + ". Cochez les GPO et les OU cibles : le rapport d'impact est calculé en temps réel.");

            UpdateImpact();
        }
        catch (Exception ex)
        {
            _logger.Error("Chargement du module de déploiement échoué", ex);
            SetStatus(true, "Erreur de chargement : " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Sélection (case à cocher) d'une GPO dans la liste.
    /// Notifie le ViewModel pour recalculer le rapport d'impact.
    /// </summary>
    public sealed class GpoSelection : ObservableObject
    {
        private readonly Action<GpoSelection> _onChanged;

        public GpoSelection(GpoSummary gpo, Action<GpoSelection> onChanged)
        {
            Gpo = gpo;
            _onChanged = onChanged;
        }

        public GpoSummary Gpo { get; }

        public Guid GpoId => Gpo.Id;

        public string DisplayName => Gpo.DisplayName;

        public string StatusText => Gpo.StatusText;

        public DateTimeOffset? ModificationTime => Gpo.ModificationTime;

        private bool _isSelected;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value))
                {
                    _onChanged(this);
                }
            }
        }
    }

    /// <summary>Sélection (case à cocher) d'une OU cible.</summary>
    public sealed class OuTarget : ObservableObject
    {
        private readonly Action<OuTarget> _onChanged;

        public OuTarget(OuMappingRow row, Action<OuTarget> onChanged)
        {
            Row = row;
            _onChanged = onChanged;
        }

        public OuMappingRow Row { get; }

        public string Name => Row.Name;

        public string DistinguishedName => Row.DistinguishedName;

        public int ComputerCount => Row.ComputerCount;

        public string? DepartmentName => Row.DepartmentName;

        private bool _isSelected;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value))
                {
                    _onChanged(this);
                }
            }
        }
    }

    private void OnSelectionChanged(object _)
    {
        UpdateImpact();
    }

    /// <summary>
    /// Recalcul du rapport d'impact : la planification est locale
    /// (les données AD DS sont déjà chargées), donc le rapport est
    /// mis à jour immédiatement à chaque changement de sélection.
    /// Les passages en concurrence sont sérialisés.
    /// </summary>
    private async void UpdateImpact()
    {
        if (_impactUpdatePending)
        {
            _impactUpdateRequested = true;
            return;
        }

        _impactUpdatePending = true;
        try
        {
            do
            {
                _impactUpdateRequested = false;
                if (!Connection.IsConnected || Connection.Current is null)
                {
                    return;
                }

                var selectedGpos = Gpos.Where(selection => selection.IsSelected)
                    .Select(selection => selection.Gpo)
                    .ToList();
                var selectedTargets = Targets.Where(target => target.IsSelected)
                    .Select(target => new OuInfo
                    {
                        Name = target.Name,
                        DistinguishedName = target.DistinguishedName,
                        CanonicalName = null,
                        ParentDistinguishedName = null,
                    })
                    .ToList();

                var result = await _deployment.PlanDeploymentAsync(
                    Connection.Current,
                    selectedGpos,
                    selectedTargets,
                    _users,
                    _computers,
                    _cachedLinks);

                if (result.IsSuccess && result.Value is not null)
                {
                    ApplyPlan(result.Value);
                }
                else
                {
                    _plan = null;
                    HasValidPlan = false;
                    ImpactEntries.Clear();
                    SelectedOuLinks.Clear();
                    AffectedUsersText = "0";
                    AffectedComputersText = "0";
                    TargetCountText = "0 cible(s)";
                    ImpactSummaryText = result.Error?.ToDisplayString()
                        ?? "Sélectionnez des GPO et des unités d'organisation.";
                }
            }
            while (_impactUpdateRequested);
        }
        catch (Exception ex)
        {
            _logger.Error("Calcul du rapport d'impact échoué", ex);
            ImpactSummaryText = "Erreur de calcul du rapport d'impact : " + ex.Message;
        }
        finally
        {
            _impactUpdatePending = false;
        }
    }

    private void ApplyPlan(GpoDeploymentPlan plan)
    {
        _plan = plan;
        HasValidPlan = plan.IsValid;

        ImpactEntries.Clear();
        foreach (var entry in plan.Entries)
        {
            ImpactEntries.Add(entry);
        }

        AffectedUsersText = plan.TotalAffectedUsers.ToString();
        AffectedComputersText = plan.TotalAffectedComputers.ToString();
        TargetCountText = plan.Entries.Count + " lien(s) prévu(s)";

        var selectedOuDns = Targets.Where(target => target.IsSelected)
            .Select(target => target.DistinguishedName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        SelectedOuLinks.Clear();
        foreach (var link in _cachedLinks
                     .Where(link => selectedOuDns.Contains(link.TargetDistinguishedName))
                     .OrderBy(link => link.TargetDistinguishedName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(link => link.Order))
        {
            SelectedOuLinks.Add(link);
        }

        ImpactSummaryText = plan.IsValid
            ? plan.Entries.Count + " lien(s) de GPO prév(s) · "
              + plan.TotalAffectedUsers + " utilisateur(s) et "
              + plan.TotalAffectedComputers + " ordinateur(s) impacté(s)"
              + (plan.Warnings.Count > 0 ? " · " + plan.Warnings.Count + " avertissement(s)" : string.Empty)
            : "Sélectionnez des GPO et des unités d'organisation.";
    }

    /// <summary>
    /// Validation puis déploiement : le plan validé est appliqué
    /// (New-GPLink par GPO × OU), après confirmation explicite.
    /// </summary>
    [RelayCommand]
    private async Task DeployAsync()
    {
        if (_plan is null || !_plan.IsValid)
        {
            SetStatus(true, "Le plan n'est pas valide : cochez au moins une GPO et une OU cible, puis attendez le calcul du rapport d'impact.");
            return;
        }

        if (Connection.Current is null)
        {
            SetStatus(true, "Aucun domaine connecté.");
            return;
        }

        var confirmation = MessageBox.Show(
            "Le déploiement va appliquer " + _plan.Entries.Count + " lien(s) de GPO :\n"
                + _plan.TotalAffectedUsers + " utilisateur(s) et "
                + _plan.TotalAffectedComputers + " ordinateur(s) impacté(s).\n\n"
                + "Confirmer l'application sur le domaine " + Connection.Current.DomainFqdn + " ?",
            "Validation du déploiement GPO",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        IsBusy = true;
        SetStatus(false, "Déploiement en cours…");

        try
        {
            var result = await _deployment.DeployAsync(Connection.Current, _plan);

            DeploymentResults.Clear();
            if (result.IsSuccess && result.Value is not null)
            {
                foreach (var operation in result.Value.Operations)
                {
                    DeploymentResults.Add(operation);
                }

                ShowResults = true;
                SetStatus(!result.Value.IsSuccess,
                    "Déploiement terminé : " + result.Value.SummaryText
                    + " Le cache local est actualisé (synchronisation).");

                // Le cache local des liens est rafraîchi : AD DS reste la source de vérité.
                var summary = await _sync.SyncAsync(Connection.Current);
                Connection.SetSyncResult(summary);
                _cachedLinks = await _inventory.GetAllLinksAsync();
            }
            else if (result.Error is not null)
            {
                SetStatus(true, result.Error.ToDisplayString());
            }

            // Le rapport d'impact est recalculé avec les liens mis à jour
            UpdateImpact();
        }
        catch (Exception ex)
        {
            _logger.Error("Déploiement échoué", ex);
            SetStatus(true, "Erreur de déploiement : " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Supprime un lien de GPO existant (Remove-GPLink).</summary>
    [RelayCommand]
    private async Task UnlinkAsync(GpoLinkInfo? link)
    {
        if (link is null || Connection.Current is null)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            "Supprimer le lien de la GPO « " + (link.GpoName ?? link.GpoId.ToString()) + " » sur la cible " + link.TargetDistinguishedName + " ?",
            "Suppression de lien GPO",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _deployment.UnlinkGpoAsync(
                Connection.Current, link.GpoId, link.TargetDistinguishedName);

            SetStatus(!result.IsSuccess,
                result.IsSuccess
                    ? "Lien supprimé : " + (result.Value ?? string.Empty)
                    : result.Error?.ToDisplayString() ?? "Échec de la suppression du lien.");

            var summary = await _sync.SyncAsync(Connection.Current);
            Connection.SetSyncResult(summary);
            _cachedLinks = await _inventory.GetAllLinksAsync();
            UpdateImpact();
        }
        catch (Exception ex)
        {
            _logger.Error("Suppression de lien échouée", ex);
            SetStatus(true, "Erreur : " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SelectAllGpos()
    {
        foreach (var selection in Gpos)
        {
            selection.IsSelected = true;
        }
    }

    [RelayCommand]
    private void ClearGpos()
    {
        foreach (var selection in Gpos)
        {
            selection.IsSelected = false;
        }
    }

    [RelayCommand]
    private void SelectAllTargets()
    {
        foreach (var target in Targets)
        {
            target.IsSelected = true;
        }
    }

    [RelayCommand]
    private void ClearTargets()
    {
        foreach (var target in Targets)
        {
            target.IsSelected = false;
        }
    }

    [RelayCommand]
    private void OpenGpoList() => _navigation.NavigateTo<GpoListViewModel>();

    [RelayCommand]
    private void OpenParc() => _navigation.NavigateTo<ParcViewModel>();

    private void SetStatus(bool isError, string message)
    {
        IsStatusError = isError;
        StatusMessage = message;
        ShowStatus = !string.IsNullOrWhiteSpace(message);
    }
}
