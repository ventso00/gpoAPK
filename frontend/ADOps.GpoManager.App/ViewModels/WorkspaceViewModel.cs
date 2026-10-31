using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using ADOps.GpoManager.App.Services;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Services;
using ADOps.GpoManager.Infrastructure.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ADOps.GpoManager.App.ViewModels;

/// <summary>
/// Le flux réel du mode démo : postes par département → catalogue GPO →
/// paramètres → application AD DS → console de vérification du poste.
/// Toutes les données proviennent d'AD DS (cache local synchronisé) ; aucune
/// simulation : chaque action appelle PowerShell, GPMC et WinRM.
/// </summary>
public sealed partial class WorkspaceViewModel : ViewModelBase
{
    private readonly InventoryQueryRepository _inventory;
    private readonly GpoDeploymentRepository _deployments;
    private readonly IGpoCatalogService _catalog;
    private readonly IGpoTemplateDeploymentService _deployment;
    private readonly IComputerControlService _computerControl;
    private readonly IInventorySyncService _sync;
    private readonly ConnectionState _connection;
    private readonly ILogger _logger;
    private CancellationTokenSource? _deployCancellation;
    private bool _reloading;

    /// <summary>Postes dont l'IP est en cours de résolution DNS (dé-duplique les requêtes).</summary>
    private readonly ConcurrentDictionary<string, byte> _ipResolutionInFlight = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<MachineItemViewModel> Machines { get; } = new();
    public ObservableCollection<DepartmentGroupViewModel> DepartmentGroups { get; } = new();
    public ObservableCollection<UserItemViewModel> Users { get; } = new();
    public ObservableCollection<UserGroupViewModel> UserGroups { get; } = new();
    public ObservableCollection<TemplateItemViewModel> Templates { get; } = new();
    public ObservableCollection<CategoryFilterViewModel> CategoryFilters { get; } = new();
    public ObservableCollection<ConsoleLineViewModel> ConsoleLines { get; } = new();
    public ObservableCollection<DeploymentResultRow> DeploymentResults { get; } = new();
    public ObservableCollection<DeployWarning> Warnings { get; } = new();
    public ObservableCollection<ClientGpoRecordViewModel> ClientRecords { get; } = new();
    public ObservableCollection<RegistryGroupViewModel> ClientRegistryGroups { get; } = new();
    public ObservableCollection<string> ClientGpoResults { get; } = new();
    public ObservableCollection<ConsoleLineViewModel> ClientEvents { get; } = new();
    public ObservableCollection<OuOptionViewModel> AvailableOus { get; } = new();

    [ObservableProperty]
    private string _activePage = "machines";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedCategory = "*";

    [ObservableProperty]
    private string _machinesSummaryText = "Aucun poste synchronisé";

    [ObservableProperty]
    private string _usersSummaryText = "Aucun utilisateur synchronisé";

    [ObservableProperty]
    private string _filteredTemplateCountText = "0 modèle";

    [ObservableProperty]
    private int _selectedMachineCount;

    [ObservableProperty]
    private int _selectedUserCount;

    [ObservableProperty]
    private int _selectedGpoCount;

    [ObservableProperty]
    private int _plannedWriteCount;

    [ObservableProperty]
    private int _totalConfiguredParameters;

    [ObservableProperty]
    private int _userScopeCount;

    [ObservableProperty]
    private int _computerScopeCount;

    [ObservableProperty]
    private string _selectionPillText = "0 machine sélectionnée";

    [ObservableProperty]
    private string _gpoSelectionPillText = "0 GPO sélectionnée";

    [ObservableProperty]
    private string _pipelineSummary = "Sélectionnez au moins un poste et une GPO.";

    [ObservableProperty]
    private string _deploySummary = "Sélectionnez au moins un poste (étape 1) et une GPO (étape 2) pour exécuter le protocole.";

    [ObservableProperty]
    private bool _isDeploying;

    [ObservableProperty]
    private double _deployProgress;

    [ObservableProperty]
    private string _deployStatusText = string.Empty;

    [ObservableProperty]
    private bool _hasDeployedResult;

    [ObservableProperty]
    private string _deployResultTitle = string.Empty;

    [ObservableProperty]
    private bool _deploySucceeded;

    [ObservableProperty]
    private bool _showStatus;

    [ObservableProperty]
    private bool _isStatusError;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private MachineItemViewModel? _currentClient;

    [ObservableProperty]
    private string _clientTab = "applied";

    [ObservableProperty]
    private bool _isClientBusy;

    [ObservableProperty]
    private string _clientStatusText = string.Empty;

    [ObservableProperty]
    private string _clientHeaderText = "Aucun poste sélectionné";

    [ObservableProperty]
    private string _clientSubText = "Après un déploiement, ouvrez un poste pour vérifier ce qui a réellement été appliqué.";

    [ObservableProperty]
    private string _clientRecordSummary = "Aucune GPO déployée par ADOps sur ce poste.";

    [ObservableProperty]
    private string _clientVerifySummary = "Lancez « Vérifier (gpresult) » pour lire les stratégies appliquées sur le poste.";

    [ObservableProperty]
    private string _targetUsersText = "Aucun compte utilisateur standard ciblé.";

    [ObservableProperty]
    private bool _isComputerControlBusy;

    [ObservableProperty]
    private string? _selectedTargetOu;

    public bool IsMachinesPage => ActivePage == "machines";
    public bool IsGposPage => ActivePage == "gpos";
    public bool IsParamsPage => ActivePage == "params";
    public bool IsDeployPage => ActivePage == "deploy";
    public bool IsClientsPage => ActivePage == "clients";

    public bool IsClientAppliedTab => ClientTab == "applied";
    public bool IsClientRegistryTab => ClientTab == "registry";
    public bool IsClientRsopTab => ClientTab == "rsop";
    public bool IsClientEventsTab => ClientTab == "events";

    public bool HasSelectedClient => CurrentClient is not null;
    public bool HasClientRecords => ClientRecords.Count > 0;
    public bool ClientHasNoRecords => CurrentClient is not null && ClientRecords.Count == 0;
    public bool CanControlClient => CurrentClient is not null && !IsClientBusy && !IsComputerControlBusy;
    public bool CanMoveClient => CanControlClient
        && !string.IsNullOrWhiteSpace(SelectedTargetOu)
        && !string.Equals(SelectedTargetOu, CurrentClient?.Row.OuDistinguishedName, StringComparison.OrdinalIgnoreCase);
    public bool CanDeploy => !IsDeploying && SelectedUserCount > 0 && SelectedGpoCount > 0;
    public bool CanContinueToGpos => SelectedUserCount > 0;
    public bool CanContinueToParams => SelectedGpoCount > 0;
    public bool HasTemplates => SelectedGpoCount > 0;
    public bool IsConnected => _connection.IsConnected;
    public string DeploymentButtonText => IsDeploying ? "Déploiement en cours…" : "▶ Exécuter le protocole du projet";

    public IReadOnlyList<TemplateItemViewModel> SelectedTemplates
        => Templates.Where(template => template.IsSelected).ToList();

    public string ConnectionDomain => _connection.Current?.DomainFqdn ?? "Aucun domaine";

    public WorkspaceViewModel(
        InventoryQueryRepository inventory,
        GpoDeploymentRepository deployments,
        IGpoCatalogService catalog,
        IGpoTemplateDeploymentService deployment,
        IComputerControlService computerControl,
        IInventorySyncService sync,
        ConnectionState connection,
        ILogger logger)
    {
        _inventory = inventory;
        _deployments = deployments;
        _catalog = catalog;
        _deployment = deployment;
        _computerControl = computerControl;
        _sync = sync;
        _connection = connection;
        _logger = logger;

        _connection.PropertyChanged += OnConnectionChanged;
        BuildCatalog(previousSelection: null, previousValues: null);
    }

    partial void OnCurrentClientChanged(MachineItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedClient));
        OnPropertyChanged(nameof(ClientHasNoRecords));
        OnPropertyChanged(nameof(CanControlClient));
        OnPropertyChanged(nameof(CanMoveClient));

        if (value is not null)
        {
            SelectedTargetOu = string.Empty;
        }
    }

    partial void OnIsClientBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanControlClient));
        OnPropertyChanged(nameof(CanMoveClient));
    }

    partial void OnIsComputerControlBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanControlClient));
        OnPropertyChanged(nameof(CanMoveClient));
    }

    partial void OnSelectedTargetOuChanged(string? value) => OnPropertyChanged(nameof(CanMoveClient));

    private void OnConnectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConnectionState.IsConnected) or nameof(ConnectionState.Current))
        {
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(CanDeploy));
            OnPropertyChanged(nameof(ConnectionDomain));
        }
    }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        _ = ReloadAsync();
    }

    /// <summary>Recharge utilisateurs, GPO et traces locales après une synchronisation AD DS.</summary>
    public async Task ReloadAsync()
    {
        if (_reloading)
        {
            return;
        }

        _reloading = true;
        IsBusy = true;
        try
        {
            var selectedUserDns = Users
                .Where(user => user.IsSelected)
                .Select(user => user.DistinguishedName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var previousSelection = Templates
                .Where(template => template.IsSelected)
                .Select(template => template.TemplateId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var previousValues = Templates.ToDictionary(
                template => template.TemplateId,
                template => template.ExportValues(),
                StringComparer.OrdinalIgnoreCase);
            var currentClientDn = CurrentClient?.DistinguishedName;
            var currentClientName = CurrentClient?.Name;
            var previousTargetOu = SelectedTargetOu;

            var userAccounts = (await _inventory.GetClientAccountsAsync().ConfigureAwait(true))
                .Where(account => account.Enabled && !account.IsAdministrator && !string.IsNullOrWhiteSpace(account.DistinguishedName))
                .ToList();
            var ous = await _inventory.GetOuMappingsAsync().ConfigureAwait(true);

            Users.Clear();
            UserGroups.Clear();
            AvailableOus.Clear();
            AvailableOus.Add(new OuOptionViewModel(string.Empty, "— Choisir une OU cible —"));
            foreach (var ou in ous
                         .Where(ou => !string.IsNullOrWhiteSpace(ou.DistinguishedName)
                                      && !ou.DistinguishedName.Contains("OU=Domain Controllers", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(ou => ou.Name, StringComparer.OrdinalIgnoreCase))
            {
                AvailableOus.Add(new OuOptionViewModel(ou.DistinguishedName, ou.Name));
            }

            SelectedTargetOu = AvailableOus
                .FirstOrDefault(ou => string.Equals(ou.DistinguishedName, previousTargetOu, StringComparison.OrdinalIgnoreCase))
                ?.DistinguishedName ?? string.Empty;

            foreach (var group in userAccounts
                         .GroupBy(account => account.Department ?? "Sans département", StringComparer.OrdinalIgnoreCase)
                         .OrderBy(group => group.Key == "Sans département" ? "\uFFFF" : group.Key, StringComparer.OrdinalIgnoreCase))
            {
                var items = group
                    .OrderBy(account => account.SamAccountName, StringComparer.OrdinalIgnoreCase)
                    .Select(account => new UserItemViewModel(
                        new UserInfo
                        {
                            SamAccountName = account.SamAccountName,
                            DistinguishedName = account.DistinguishedName,
                            DisplayName = account.DisplayName,
                            GivenName = account.GivenName,
                            Surname = account.Surname,
                            Email = account.Email,
                            Title = account.Title,
                            Department = account.Department,
                            Enabled = account.Enabled,
                            LastLogonUtc = account.LastLogonUtc,
                            IsAdministrator = account.IsAdministrator,
                            AccountLocked = account.AccountLocked,
                            PasswordNeverExpires = account.PasswordNeverExpires,
                            OuDistinguishedName = account.OuDistinguishedName,
                        },
                        selectedUserDns.Contains(account.DistinguishedName),
                        OnUserSelectionChanged))
                    .ToList();

                foreach (var user in items)
                {
                    Users.Add(user);
                }

                var ouNames = group
                    .Select(account => account.OuName ?? account.OuDistinguishedName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(3)
                    .ToList();
                UserGroups.Add(new UserGroupViewModel(
                    group.Key,
                    ouNames.Count == 0 ? "Annuaire Active Directory" : string.Join(" · ", ouNames),
                    items));
            }

            UsersSummaryText = Users.Count + (Users.Count > 1 ? " comptes" : " compte")
                + " · " + Users.Count(user => user.IsAccountEnabled) + " actif(s)"
                + " · " + UserGroups.Count + (UserGroups.Count > 1 ? " départements" : " département");

            // Charger aussi les postes pour la section clients (optionnel)
            var computers = (await _inventory.GetComputersAsync().ConfigureAwait(true))
                .Where(IsClientComputer)
                .ToList();
            Machines.Clear();
            DepartmentGroups.Clear();
            foreach (var group in computers
                         .GroupBy(ComputerDepartment, StringComparer.OrdinalIgnoreCase)
                         .OrderBy(group => group.Key == "Sans département" ? "\uFFFF" : group.Key, StringComparer.OrdinalIgnoreCase))
            {
                var items = group
                    .OrderBy(computer => computer.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(computer => new MachineItemViewModel(
                        computer,
                        false,
                        OnMachineSelectionChanged))
                    .ToList();

                foreach (var machine in items)
                {
                    Machines.Add(machine);
                }

                var ouNames = group
                    .Select(computer => computer.OuName ?? computer.OuDistinguishedName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(3)
                    .ToList();
                DepartmentGroups.Add(new DepartmentGroupViewModel(
                    group.Key,
                    ouNames.Count == 0 ? "Annuaire Active Directory" : string.Join(" · ", ouNames),
                    items));
            }

            MachinesSummaryText = Machines.Count + (Machines.Count > 1 ? " postes" : " poste")
                + " · " + Machines.Count(machine => machine.IsOnline) + " en ligne"
                + " · " + DepartmentGroups.Count + (DepartmentGroups.Count > 1 ? " départements" : " département");

            // Résolution DNS en parallèle des IP manquantes (best-effort, hors chemin UI).
            _ = ResolveIpsForAllAsync(Machines);

            BuildCatalog(previousSelection, previousValues);
            await ReloadDeploymentRecordsAsync().ConfigureAwait(true);
            ApplySearch();
            RefreshSelection();

            if (currentClientDn is not null)
            {
                var refreshed = Machines.FirstOrDefault(machine =>
                    string.Equals(machine.DistinguishedName, currentClientDn, StringComparison.OrdinalIgnoreCase))
                    ?? Machines.FirstOrDefault(machine =>
                        string.Equals(machine.Name, currentClientName, StringComparison.OrdinalIgnoreCase));
                if (refreshed is not null)
                {
                    await SelectClientAsync(refreshed).ConfigureAwait(true);
                }
                else
                {
                    CurrentClient = null;
                    OnPropertyChanged(nameof(HasSelectedClient));
                    ResetClientConsole();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Chargement de l'espace de travail échoué", ex);
            SetStatus(true, "Impossible de charger le parc : " + ex.Message);
        }
        finally
        {
            _reloading = false;
            IsBusy = false;
        }
    }

    private static bool IsClientComputer(ComputerRow computer)
    {
        if (computer.DistinguishedName.Contains("OU=Domain Controllers", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return computer.OperatingSystem?.Contains("Server", StringComparison.OrdinalIgnoreCase) != true;
    }

    private static string ComputerDepartment(ComputerRow computer)
    {
        if (!string.IsNullOrWhiteSpace(computer.DepartmentName))
        {
            return computer.DepartmentName!;
        }

        if (!string.IsNullOrWhiteSpace(computer.OuName))
        {
            return computer.OuName!;
        }

        return "Sans département";
    }

    private void BuildCatalog(
        IReadOnlyCollection<string>? previousSelection,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? previousValues)
    {
        Templates.Clear();

        foreach (var template in _catalog.GetTemplates())
        {
            var binding = _catalog.GetBinding(template.TemplateId);
            IReadOnlyDictionary<string, string>? values = null;
            previousValues?.TryGetValue(template.TemplateId, out values);
            var item = new TemplateItemViewModel(
                template,
                binding,
                _catalog,
                values,
                OnTemplateSelectionChanged,
                OnTemplateSettingsChanged);
            item.IsSelected = previousSelection?.Contains(template.TemplateId) == true;
            Templates.Add(item);
        }

        var previousCategory = SelectedCategory;
        CategoryFilters.Clear();
        CategoryFilters.Add(new CategoryFilterViewModel("*", "Toutes")
        {
            IsActive = previousCategory == "*",
        });

        foreach (var category in Templates
                     .Select(template => template.Category)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(category => category, StringComparer.OrdinalIgnoreCase))
        {
            CategoryFilters.Add(new CategoryFilterViewModel(category, category)
            {
                IsActive = string.Equals(category, previousCategory, StringComparison.OrdinalIgnoreCase),
            });
        }

        if (CategoryFilters.All(filter => !filter.IsActive))
        {
            SelectedCategory = "*";
            CategoryFilters[0].IsActive = true;
        }

        ApplySearch();
    }

    private async Task ReloadDeploymentRecordsAsync()
    {
        var records = await _deployments.GetAllAsync().ConfigureAwait(true);
        var counts = records
            .GroupBy(record => record.ComputerDistinguishedName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var user in Users)
        {
            user.RecordCount = counts.TryGetValue(user.DistinguishedName, out var count) ? count : 0;
        }

        foreach (var machine in Machines)
        {
            machine.RecordCount = counts.TryGetValue(machine.DistinguishedName, out var count) ? count : 0;
        }
    }

    private void OnMachineSelectionChanged(MachineItemViewModel machine)
    {
        foreach (var group in DepartmentGroups)
        {
            if (group.Machines.Contains(machine))
            {
                group.Recount();
                break;
            }
        }

        RefreshSelection();
    }

    private void OnUserSelectionChanged(UserItemViewModel user)
    {
        foreach (var group in UserGroups)
        {
            if (group.Users.Contains(user))
            {
                group.Recount();
                break;
            }
        }

        RefreshSelection();
    }

    private void OnTemplateSelectionChanged()
    {
        RefreshSelection();
    }

    private void OnTemplateSettingsChanged()
    {
        RefreshSelection();
    }

    private void RefreshSelection()
    {
        SelectedMachineCount = Machines.Count(machine => machine.IsSelected);
        SelectedUserCount = Users.Count(user => user.IsSelected);
        SelectedGpoCount = Templates.Count(template => template.IsSelected);
        SelectionPillText = SelectedUserCount + (SelectedUserCount > 1 ? " utilisateurs sélectionnés" : " utilisateur sélectionné")
            + "   ·   " + SelectedMachineCount + (SelectedMachineCount > 1 ? " poste(s)" : " poste");
        GpoSelectionPillText = SelectedGpoCount + (SelectedGpoCount > 1 ? " GPO sélectionnées" : " GPO sélectionnée");

        var selectedTemplates = SelectedTemplates;
        TotalConfiguredParameters = selectedTemplates.Sum(template => template.Parameters.Count);
        UserScopeCount = selectedTemplates.Count(template => template.IsUserScope);
        ComputerScopeCount = selectedTemplates.Count - UserScopeCount;
        var settingsPerUser = selectedTemplates.Where(template => !template.HasError).Sum(template => template.SettingCount);
        PlannedWriteCount = settingsPerUser * SelectedUserCount;

        PipelineSummary = SelectedUserCount == 0 || SelectedGpoCount == 0
            ? "Sélectionnez au moins un utilisateur (étape 1) et une GPO (étape 2)."
            : SelectedUserCount + " utilisateur(s) × " + SelectedGpoCount + " GPO → "
              + PlannedWriteCount + " écriture(s) de registre prévue(s), puis actualisation et validation automatiques.";

        DeploySummary = SelectedUserCount > 0 && SelectedGpoCount > 0
            ? SelectedUserCount + " utilisateur(s) × " + SelectedGpoCount
              + " GPO → liaison OU transparente, filtrage AD DS, paramètres et validation automatiques."
            : "Sélectionnez au moins un utilisateur (étape 1) et une GPO (étape 2) pour exécuter le protocole.";

        OnPropertyChanged(nameof(SelectedTemplates));
        OnPropertyChanged(nameof(CanDeploy));
        OnPropertyChanged(nameof(CanContinueToGpos));
        OnPropertyChanged(nameof(CanContinueToParams));
        OnPropertyChanged(nameof(HasTemplates));
        UpdateWarnings();
    }

    private void UpdateWarnings()
    {
        Warnings.Clear();

        if (SelectedUserCount == 0 || SelectedGpoCount == 0)
        {
            Warnings.Add(new DeployWarning
            {
                IsError = true,
                Message = "Plan incomplet : sélectionnez au moins un utilisateur et une GPO.",
            });
        }

        foreach (var template in SelectedTemplates.Where(template => template.IsHighRisk))
        {
            Warnings.Add(new DeployWarning
            {
                IsError = true,
                Message = template.DisplayName + " présente un risque élevé : relisez ses paramètres avant de confirmer.",
            });
        }

        if (UserScopeCount > 0)
        {
            Warnings.Add(new DeployWarning
            {
                Message = UserScopeCount + " GPO de portée Utilisateur : elles seront liées aux OU des comptes standards "
                    + "des départements sélectionnés, puis filtrées sur ces seuls comptes. Les administrateurs sont exclus.",
            });
        }

        _ = UpdateTargetUsersTextAsync();
    }

    private async Task UpdateTargetUsersTextAsync()
    {
        try
        {
            var count = await CountTargetUsersAsync().ConfigureAwait(true);
            TargetUsersText = count == 0
                ? "Aucun compte standard dans les départements ciblés : les GPO Utilisateur ne pourront pas être liées."
                : count + " compte(s) standard ciblé(s) pour les GPO Utilisateur.";
        }
        catch
        {
            TargetUsersText = "Comptes utilisateurs non vérifiés.";
        }
    }

    private async Task<int> CountTargetUsersAsync()
    {
        return Users.Count(user => user.IsSelected && user.IsAccountEnabled && !user.IsAdministrator);
    }

    private async Task<IReadOnlyList<UserInfo>> LoadTargetUsersAsync()
    {
        var departments = Machines
            .Where(machine => machine.IsSelected)
            .Select(machine => machine.Department)
            .Where(department => !string.IsNullOrWhiteSpace(department) && department != "Sans département")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var users = new List<UserInfo>();
        foreach (var department in departments)
        {
            var accounts = await _inventory.GetClientAccountsAsync(department: department, onlyStandardAccounts: true)
                .ConfigureAwait(true);
            foreach (var account in accounts)
            {
                if (!account.Enabled || string.IsNullOrWhiteSpace(account.DistinguishedName))
                {
                    continue;
                }

                users.Add(new UserInfo
                {
                    SamAccountName = account.SamAccountName,
                    DistinguishedName = account.DistinguishedName,
                    DisplayName = account.DisplayName,
                    GivenName = account.GivenName,
                    Surname = account.Surname,
                    Email = account.Email,
                    Title = account.Title,
                    Department = account.Department,
                    Enabled = account.Enabled,
                    LastLogonUtc = account.LastLogonUtc,
                    IsAdministrator = account.IsAdministrator,
                });
            }
        }

        return users
            .GroupBy(user => user.DistinguishedName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    partial void OnActivePageChanged(string value)
    {
        OnPropertyChanged(nameof(IsMachinesPage));
        OnPropertyChanged(nameof(IsGposPage));
        OnPropertyChanged(nameof(IsParamsPage));
        OnPropertyChanged(nameof(IsDeployPage));
        OnPropertyChanged(nameof(IsClientsPage));
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplySearch();
    }

    partial void OnClientTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsClientAppliedTab));
        OnPropertyChanged(nameof(IsClientRegistryTab));
        OnPropertyChanged(nameof(IsClientRsopTab));
        OnPropertyChanged(nameof(IsClientEventsTab));
    }

    partial void OnIsDeployingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanDeploy));
        OnPropertyChanged(nameof(DeploymentButtonText));
    }

    private void ApplySearch()
    {
        var query = SearchText.Trim().ToLowerInvariant();

        foreach (var template in Templates)
        {
            var categoryMatch = SelectedCategory == "*"
                || string.Equals(template.Category, SelectedCategory, StringComparison.OrdinalIgnoreCase);
            template.IsVisible = categoryMatch
                && (query.Length == 0 || template.SearchBlob.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var group in DepartmentGroups)
        {
            foreach (var machine in group.Machines)
            {
                machine.IsVisible = query.Length == 0
                    || machine.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || machine.Department.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || machine.OuText.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || machine.IpText.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || machine.OsText.Contains(query, StringComparison.OrdinalIgnoreCase);
            }

            group.IsVisible = group.Machines.Any(machine => machine.IsVisible);
        }

        foreach (var group in UserGroups)
        {
            foreach (var user in group.Users)
            {
                user.IsVisible = query.Length == 0
                    || user.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || user.Login.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || user.Department.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || user.OuText.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || user.Email.Contains(query, StringComparison.OrdinalIgnoreCase);
            }

            group.IsVisible = group.Users.Any(user => user.IsVisible);
        }

        var visible = Templates.Count(template => template.IsVisible);
        FilteredTemplateCountText = query.Length > 0
            ? visible + " correspondance(s) sur " + Templates.Count + " · recherche : « " + SearchText.Trim() + " »"
            : visible + " modèle(s) affiché(s) · " + Templates.Count + " au catalogue";
    }

    [RelayCommand]
    private void SetPage(string? page)
    {
        if (string.IsNullOrWhiteSpace(page))
        {
            return;
        }

        if (page == "clients" && CurrentClient is null && Machines.Count > 0)
        {
            CurrentClient = Machines.FirstOrDefault(machine => machine.IsSelected) ?? Machines[0];
            _ = SelectClientAsync(CurrentClient);
        }

        ActivePage = page;
        ApplySearch();
    }

    [RelayCommand]
    private void SetCategoryFilter(string? category)
    {
        SelectedCategory = string.IsNullOrWhiteSpace(category) ? "*" : category;
        foreach (var filter in CategoryFilters)
        {
            filter.IsActive = string.Equals(filter.Category, SelectedCategory, StringComparison.OrdinalIgnoreCase);
        }

        ApplySearch();
    }

    [RelayCommand]
    private void SelectAllMachines()
    {
        foreach (var machine in Machines.Where(machine => machine.IsVisible))
        {
            machine.IsSelected = true;
        }
    }

    [RelayCommand]
    private void SelectOnlineMachines()
    {
        foreach (var machine in Machines)
        {
            machine.IsSelected = machine.IsVisible && machine.IsOnline;
        }
    }

    [RelayCommand]
    private void SelectNoMachines()
    {
        foreach (var machine in Machines)
        {
            machine.IsSelected = false;
        }
    }

    [RelayCommand]
    private void SelectNoTemplates()
    {
        foreach (var template in Templates)
        {
            template.IsSelected = false;
        }
    }

    [RelayCommand]
    private Task ReloadWorkspace() => ReloadAsync();

    [RelayCommand]
    private void ToggleDepartment(DepartmentGroupViewModel? group)
    {
        if (group is null)
        {
            return;
        }

        var select = !group.IsAllSelected;
        foreach (var machine in group.Machines)
        {
            machine.IsSelected = select;
        }

        group.Recount();
    }

    [RelayCommand]
    private void SelectAllUsers()
    {
        foreach (var user in Users.Where(user => user.IsVisible))
        {
            user.IsSelected = true;
        }
    }

    [RelayCommand]
    private void SelectNoUsers()
    {
        foreach (var user in Users)
        {
            user.IsSelected = false;
        }
    }

    [RelayCommand]
    private void ToggleUserDepartment(UserGroupViewModel? group)
    {
        if (group is null)
        {
            return;
        }

        var select = !group.IsAllSelected;
        foreach (var user in group.Users)
        {
            user.IsSelected = select;
        }

        group.Recount();
    }

    [RelayCommand]
    private void DismissStatus() => ShowStatus = false;

    [RelayCommand]
    private async Task DeployAsync()
    {
        if (!CanDeploy)
        {
            SetStatus(true, "Sélectionnez au moins un utilisateur et une GPO avant de déployer.");
            return;
        }

        var context = _connection.Current;
        if (context is null)
        {
            SetStatus(true, "Aucune session de domaine active : connectez la session Windows à un domaine, puis synchronisez.");
            return;
        }

        var templates = SelectedTemplates.Where(template => !template.HasError).ToList();
        if (templates.Count == 0)
        {
            SetStatus(true, "Aucune GPO déployable : corrigez les paramètres signalés en rouge.");
            return;
        }

        var selectedUsers = Users.Where(user => user.IsSelected).ToList();
        IsDeploying = true;
        HasDeployedResult = false;
        DeployProgress = 0;
        DeployStatusText = "Préparation du protocole…";
        ConsoleLines.Clear();
        DeploymentResults.Clear();
        ShowStatus = false;
        _deployCancellation = new CancellationTokenSource();

        ConsoleLines.Add(ConsoleLineViewModel.Command(
            "$targets = \"" + string.Join("\",\"", selectedUsers.Select(user => user.Name)) + "\" ; Invoke-GpoDeployment -Gpo "
            + templates.Count + " -Verbose"));

        try
        {
            var users = selectedUsers.Select(user => user.Row).ToList();
            SetStatus(false, "Déploiement en cours…");

            var progress = new Progress<ExecutionLine>(line =>
            {
                ConsoleLines.Add(ConsoleLineViewModel.From(line));
                DeployProgress = Math.Min(92, DeployProgress + 3);
                DeployStatusText = line.Message;
            });

            var selections = templates
                .Select(template => new TemplateDeploymentSelection
                {
                    TemplateId = template.TemplateId,
                    Parameters = template.BuildParameters(),
                })
                .ToList();

            var result = await _deployment.DeployAsync(
                context,
                selections,
                users,
                applySecurityFiltering: true,
                progress,
                _deployCancellation.Token).ConfigureAwait(true);

            if (!result.IsSuccess || result.Value is null)
            {
                var message = result.Error?.ToDisplayString() ?? "Déploiement échoué.";
                ConsoleLines.Add(ConsoleLineViewModel.Error(message));
                DeployProgress = 100;
                DeployStatusText = "Déploiement interrompu.";
                SetStatus(true, message);
                return;
            }

            var value = result.Value;
            foreach (var record in value.Records)
            {
                await _deployments.UpsertAsync(record).ConfigureAwait(true);
            }

            var gpoErrors = value.Outcomes
                .Where(outcome => !outcome.IsSuccess)
                .Select(outcome => outcome.GpoName + " : " + outcome.Error)
                .ToList();

            foreach (var user in selectedUsers)
            {
                var refreshed = value.RefreshedComputers.Contains(user.Login, StringComparer.OrdinalIgnoreCase);
                var failedRefresh = value.FailedComputers.Contains(user.Login, StringComparer.OrdinalIgnoreCase);
                var detail = failedRefresh
                    ? "GPO liée, mais gpupdate/gpresult a échoué (poste éteint, WinRM ou DNS indisponible)."
                    : refreshed
                        ? "GPO liée et stratégie actualisée ; gpresult ne remonte pas d'erreur."
                        : "GPO liée ; actualisation non confirmée.";

                if (gpoErrors.Count > 0)
                {
                    detail += " Modèle(s) en échec : " + string.Join(" | ", gpoErrors);
                }

                DeploymentResults.Add(new DeploymentResultRow
                {
                    MachineName = user.Name,
                    IsSuccess = !failedRefresh && gpoErrors.Count == 0,
                    Detail = detail,
                });
            }

            await ReloadDeploymentRecordsAsync().ConfigureAwait(true);
            DeployProgress = 100;
            DeploySucceeded = DeploymentResults.Count(row => row.IsSuccess) == DeploymentResults.Count && DeploymentResults.Count > 0;
            DeployResultTitle = DeploySucceeded
                ? "Déploiement réussi — " + value.SummaryText
                : "Déploiement partiel — " + value.SummaryText;
            DeployStatusText = value.SummaryText;
            HasDeployedResult = true;

            ConsoleLines.Add(ConsoleLineViewModel.Success(
                "Terminé : " + value.GpoCount + " GPO, " + value.TotalSettings + " valeur(s), "
                + value.RefreshedComputers.Count + " poste(s) actualisé(s)."));

            SetStatus(!DeploySucceeded, value.SummaryText);
            RefreshSelection();

            if (CurrentClient is not null)
            {
                await SelectClientAsync(CurrentClient).ConfigureAwait(true);
            }
            else if (selectedUsers.Count > 0)
            {
                await SelectClientAsync(null).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            DeployStatusText = "Déploiement annulé par l'administrateur.";
            ConsoleLines.Add(ConsoleLineViewModel.Warning("Déploiement annulé."));
            SetStatus(true, "Déploiement annulé.");
        }
        catch (Exception ex)
        {
            _logger.Error("Déploiement par modèles échoué", ex);
            ConsoleLines.Add(ConsoleLineViewModel.Error(ex.Message));
            DeployStatusText = "Déploiement échoué.";
            SetStatus(true, "Déploiement échoué : " + ex.Message);
        }
        finally
        {
            IsDeploying = false;
            _deployCancellation?.Dispose();
            _deployCancellation = null;
        }
    }

    [RelayCommand]
    private void CancelDeploy()
    {
        _deployCancellation?.Cancel();
    }

    [RelayCommand]
    private void GoToClients() => SetPage("clients");

    /// <summary>Ouvre la console d'un poste (bouton « Console » de l'étape 1).</summary>
    [RelayCommand]
    private async Task OpenClientConsoleAsync(MachineItemViewModel? machine)
    {
        if (machine is null)
        {
            return;
        }

        ActivePage = "clients";
        await SelectClientAsync(machine).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SelectClientAsync(MachineItemViewModel? machine)
    {
        if (machine is null)
        {
            return;
        }

        foreach (var candidate in Machines)
        {
            candidate.IsCurrentClient = ReferenceEquals(candidate, machine);
        }

        CurrentClient = machine;
        ClientStatusText = string.Empty;
        ClientHeaderText = machine.Name;
        await ResolveIpAsync(machine).ConfigureAwait(true);
        ClientSubText = machine.IpText + " · " + machine.OsText + " · " + machine.OuText;
        ClientGpoResults.Clear();
        ClientEvents.Clear();
        ClientTab = "applied";
        IsClientBusy = true;

        try
        {
            var records = await _deployments.GetForComputerAsync(machine.DistinguishedName).ConfigureAwait(true);
            ClientRecords.Clear();
            foreach (var record in records)
            {
                var templateName = _catalog.GetById(record.TemplateId)?.DisplayName ?? record.TemplateId;
                ClientRecords.Add(new ClientGpoRecordViewModel(record, templateName));
            }

            ClientRecordSummary = ClientRecords.Count == 0
                ? "Aucune GPO déployée par ADOps sur ce poste. Les GPO du domaine restent visibles via gpresult."
                : ClientRecords.Count + " GPO déployée(s) par ADOps sur ce poste.";

            BuildRegistryGroups();
            OnPropertyChanged(nameof(HasClientRecords));
            OnPropertyChanged(nameof(ClientHasNoRecords));

            foreach (var record in ClientRecords.OrderBy(record => record.DeployedText))
            {
                ClientEvents.Add(ConsoleLineViewModel.Info(
                    "GPO « " + record.GpoName + " » déployée le " + record.DeployedText
                    + " (" + record.SettingsCountText + ", groupe " + record.FilterGroupText + ")."));
            }

            if (ClientRecords.Count == 0)
            {
                ClientEvents.Add(ConsoleLineViewModel.Info(
                    "Aucune trace locale. Utilisez « Vérifier (gpresult) » pour lire les stratégies réellement appliquées par le domaine."));
            }

            ClientEvents.Add(ConsoleLineViewModel.Info("Poste " + machine.StatusText + " · " + machine.LastLogonText));
        }
        catch (Exception ex)
        {
            _logger.Error("Chargement de la console du poste échoué", ex);
            ClientStatusText = "Erreur de lecture des traces locales : " + ex.Message;
        }
        finally
        {
            IsClientBusy = false;
        }
    }

    /// <summary>Résout l'IP de chaque poste sans adresse connue (DNS, best-effort, hors chemin UI).</summary>
    private async Task ResolveIpsForAllAsync(IEnumerable<MachineItemViewModel> machines)
    {
        await Task.WhenAll(machines.Select(ResolveIpAsync)).ConfigureAwait(false);
    }

    /// <summary>
    /// Résolution DNS live du poste : nom court d'abord (les suffixes du jeton
    /// s'appliquent), puis FQDN reconstitué depuis le DN de l'OU. Null si rien ne
    /// résout — l'affichage garde alors l'adresse déclarative AD ou « IP inconnue ».
    /// </summary>
    private async Task ResolveIpAsync(MachineItemViewModel machine)
    {
        if (!string.IsNullOrWhiteSpace(machine.Row.EffectiveIpAddress))
        {
            return;
        }

        if (!_ipResolutionInFlight.TryAdd(machine.Name, 0))
        {
            return;
        }

        try
        {
            var candidates = CandidateNames(machine);
            foreach (var name in candidates)
            {
                var address = await ResolveAddressAsync(name).ConfigureAwait(false);
                if (address is not null)
                {
                    machine.LiveIpAddress = address;
                    if (CurrentClient is not null && ReferenceEquals(CurrentClient, machine))
                    {
                        ClientSubText = machine.IpText + " · " + machine.OsText + " · " + machine.OuText;
                    }

                    return;
                }
            }
        }
        finally
        {
            _ipResolutionInFlight.TryRemove(machine.Name, out _);
        }
    }

    private static IEnumerable<string> CandidateNames(MachineItemViewModel machine)
    {
        yield return machine.Name;
        var domain = DnDomainFromDistinguishedName(machine.DistinguishedName);
        if (!string.IsNullOrWhiteSpace(domain))
        {
            yield return machine.Name + "." + domain;
        }
    }

    private static string? DnDomainFromDistinguishedName(string dn)
    {
        var parts = dn.Split(',');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("DC=", StringComparison.OrdinalIgnoreCase) && trimmed.Length > 3)
            {
                var rest = parts
                    .Where(p => p.Trim().StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
                    .Select(p => p.Trim()[3..])
                    .ToArray();
                return string.Join(".", rest);
            }
        }

        return null;
    }

    private static async Task<string?> ResolveAddressAsync(string host)
    {
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var addresses = await Dns.GetHostAddressesAsync(host, timeoutCts.Token).ConfigureAwait(false);
            return addresses
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.ToString())
                .FirstOrDefault()
                ?? addresses.FirstOrDefault()?.ToString();
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return null;
        }
    }

    private void BuildRegistryGroups()
    {
        ClientRegistryGroups.Clear();
        var allValues = ClientRecords.SelectMany(record => record.Values).ToList();
        foreach (var group in allValues
                     .GroupBy(value => value.Key, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            ClientRegistryGroups.Add(new RegistryGroupViewModel
            {
                Key = group.Key,
                Values = group.ToList(),
            });
        }
    }

    [RelayCommand]
    private void SetClientTab(string? tab)
    {
        if (string.IsNullOrWhiteSpace(tab))
        {
            return;
        }

        ClientTab = tab;
    }

    [RelayCommand]
    private async Task RefreshClientAsync()
    {
        var machine = CurrentClient;
        var context = _connection.Current;
        if (machine is null || context is null)
        {
            return;
        }

        IsClientBusy = true;
        ClientStatusText = "gpupdate /force en cours sur « " + machine.Name + " »…";
        ClientEvents.Add(ConsoleLineViewModel.Command("gpupdate /force — " + machine.Name));
        try
        {
            var result = await _deployment.RefreshComputerAsync(context, machine.Name).ConfigureAwait(true);
            if (result.IsSuccess)
            {
                ClientStatusText = result.Value ?? "Stratégie actualisée.";
                ClientEvents.Add(ConsoleLineViewModel.Success(result.Value ?? "Stratégie actualisée."));
                await VerifyClientCoreAsync(machine, announce: false).ConfigureAwait(true);
            }
            else
            {
                ClientStatusText = result.Error?.ToDisplayString() ?? "Actualisation impossible.";
                ClientEvents.Add(ConsoleLineViewModel.Error(ClientStatusText));
            }
        }
        catch (Exception ex)
        {
            ClientStatusText = "Actualisation impossible : " + ex.Message;
            ClientEvents.Add(ConsoleLineViewModel.Error(ClientStatusText));
        }
        finally
        {
            IsClientBusy = false;
        }
    }

    [RelayCommand]
    private async Task VerifyClientAsync()
    {
        if (CurrentClient is null)
        {
            return;
        }

        IsClientBusy = true;
        await VerifyClientCoreAsync(CurrentClient, announce: true).ConfigureAwait(true);
        IsClientBusy = false;
    }

    private async Task VerifyClientCoreAsync(MachineItemViewModel machine, bool announce)
    {
        var context = _connection.Current;
        if (context is null)
        {
            ClientStatusText = "Aucune session de domaine active.";
            return;
        }

        if (announce)
        {
            ClientStatusText = "Lecture de gpresult /r sur « " + machine.Name + " »…";
            ClientEvents.Add(ConsoleLineViewModel.Command("gpresult /r — " + machine.Name));
        }

        try
        {
            var result = await _deployment.VerifyComputerAsync(context, machine.Name).ConfigureAwait(true);
            if (!result.IsSuccess || result.Value is null)
            {
                ClientStatusText = result.Error?.ToDisplayString() ?? "Vérification impossible.";
                ClientEvents.Add(ConsoleLineViewModel.Error(ClientStatusText));
                return;
            }

            ClientGpoResults.Clear();
            foreach (var name in result.Value)
            {
                ClientGpoResults.Add(name);
            }

            ClientVerifySummary = result.Value.Count == 0
                ? "gpresult n'a remonté aucune GPO appliquée (poste hors domaine ou stratégie non traitée)."
                : result.Value.Count + " GPO appliquée(s) lue(s) sur le poste.";
            ClientStatusText = ClientVerifySummary;
            ClientEvents.Add(ConsoleLineViewModel.Success(ClientVerifySummary));
            ClientTab = "rsop";
        }
        catch (Exception ex)
        {
            ClientStatusText = "Vérification impossible : " + ex.Message;
            ClientEvents.Add(ConsoleLineViewModel.Error(ClientStatusText));
        }
    }

    [RelayCommand]
    private async Task DetachRecordAsync(ClientGpoRecordViewModel? record)
    {
        var machine = CurrentClient;
        var context = _connection.Current;
        if (machine is null || record is null || context is null)
        {
            return;
        }

        if (!record.CanDetach)
        {
            SetStatus(true, "Cette GPO est liée à l'OU sans filtre de sécurité : détachez le lien GPO depuis GPMC.");
            return;
        }

        var confirm = MessageBox.Show(
            "Détacher « " + record.GpoName + " » de « " + machine.Name + " » ?\n\n"
            + "Le poste sera retiré du groupe de filtrage AD DS, puis sa stratégie sera actualisée.\n"
            + "La GPO elle-même reste dans le domaine et continue de s'appliquer aux autres postes ciblés.",
            "ADOps GPO Manager — Détachement",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        IsClientBusy = true;
        ClientStatusText = "Détachement de « " + record.GpoName + " »…";
        ClientEvents.Add(ConsoleLineViewModel.Command(
            "Remove-ADGroupMember -Identity " + record.FilterGroupText + " — " + machine.Name));
        try
        {
            var result = await _deployment.DetachFromUserAsync(context, record.Record).ConfigureAwait(true);
            if (result.IsSuccess)
            {
                await _deployments.DeleteAsync(record.Record.GpoId, record.Record.ComputerDistinguishedName).ConfigureAwait(true);
                ClientEvents.Add(ConsoleLineViewModel.Success(result.Value ?? "Poste détaché."));
                ClientStatusText = result.Value ?? "Poste détaché.";
                await SelectClientAsync(machine).ConfigureAwait(true);
                await ReloadDeploymentRecordsAsync().ConfigureAwait(true);
            }
            else
            {
                ClientStatusText = result.Error?.ToDisplayString() ?? "Détachement impossible.";
                ClientEvents.Add(ConsoleLineViewModel.Error(ClientStatusText));
            }
        }
        catch (Exception ex)
        {
            ClientStatusText = "Détachement impossible : " + ex.Message;
            ClientEvents.Add(ConsoleLineViewModel.Error(ClientStatusText));
        }
        finally
        {
            IsClientBusy = false;
        }
    }

    [RelayCommand]
    private async Task DetachAllRecordsAsync()
    {
        var machine = CurrentClient;
        var context = _connection.Current;
        if (machine is null || context is null || ClientRecords.Count == 0)
        {
            return;
        }

        var detachable = ClientRecords.Where(record => record.CanDetach).ToList();
        if (detachable.Count == 0)
        {
            SetStatus(true, "Aucune GPO filtrable à détacher sur ce poste.");
            return;
        }

        var confirm = MessageBox.Show(
            "Détacher les " + detachable.Count + " GPO ADOps de « " + machine.Name + " » ?\n\n"
            + "Le poste sera retiré de chaque groupe de filtrage, puis gpupdate /force sera exécuté.",
            "ADOps GPO Manager — Détachement global",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        IsClientBusy = true;
        var removed = 0;
        foreach (var record in detachable)
        {
            try
            {
                var result = await _deployment.DetachFromUserAsync(context, record.Record).ConfigureAwait(true);
                if (result.IsSuccess)
                {
                    await _deployments.DeleteAsync(record.Record.GpoId, record.Record.ComputerDistinguishedName)
                        .ConfigureAwait(true);
                    removed++;
                    ClientEvents.Add(ConsoleLineViewModel.Success("« " + record.GpoName + " » détachée."));
                }
                else
                {
                    ClientEvents.Add(ConsoleLineViewModel.Error(
                        "« " + record.GpoName + " » : " + (result.Error?.ToDisplayString() ?? "échec")));
                }
            }
            catch (Exception ex)
            {
                ClientEvents.Add(ConsoleLineViewModel.Error("« " + record.GpoName + " » : " + ex.Message));
            }
        }

        ClientStatusText = removed + " / " + detachable.Count + " GPO détachée(s).";
        await SelectClientAsync(machine).ConfigureAwait(true);
        await ReloadDeploymentRecordsAsync().ConfigureAwait(true);
        IsClientBusy = false;
        SetStatus(removed != detachable.Count, ClientStatusText);
    }

    [RelayCommand]
    private void ShowClientApplied() => ClientTab = "applied";

    [RelayCommand]
    private void ShowClientRegistry() => ClientTab = "registry";

    [RelayCommand]
    private void ShowClientRsop() => ClientTab = "rsop";

    [RelayCommand]
    private void ShowClientEvents() => ClientTab = "events";

    [RelayCommand]
    private async Task ToggleComputerAccountAsync()
    {
        var machine = CurrentClient;
        var context = _connection.Current;
        if (machine is null || context is null || IsComputerControlBlocked())
        {
            return;
        }

        var disable = machine.Row.Enabled;
        var actionLabel = disable ? "Désactiver" : "Activer";
        var confirm = MessageBox.Show(
            actionLabel + " le compte d'ordinateur « " + machine.Name + " » dans AD DS ?\n\n"
            + (disable
                ? "Le poste ne pourra plus s'authentifier ni recevoir de stratégie tant que le compte reste désactivé."
                : "Le compte sera réactivé : le poste pourra de nouveau s'authentifier au domaine et recevoir ses stratégies.")
            + "\n\nL'opération est auditée.",
            "ADOps GPO Manager — " + actionLabel + " le compte",
            MessageBoxButton.YesNo,
            disable ? MessageBoxImage.Warning : MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        IsComputerControlBusy = true;
        ClientEvents.Add(ConsoleLineViewModel.Command(
            (disable ? "Disable-ADAccount" : "Enable-ADAccount") + " -Identity '" + machine.DistinguishedName + "'"));
        ClientStatusText = actionLabel + " du compte de « " + machine.Name + " »…";
        try
        {
            var result = await _computerControl
                .SetEnabledAsync(context, machine.ToComputerInfo(), !disable)
                .ConfigureAwait(true);
            if (!result.IsSuccess || result.Value is null)
            {
                FailClientControl(result.Error?.ToDisplayString() ?? "Opération impossible.");
                return;
            }

            var message = result.Value;
            await SyncAfterControlAsync().ConfigureAwait(true);
            ClientStatusText = message;
            ClientEvents.Add(ConsoleLineViewModel.Success(message));
            SetStatus(false, message);
        }
        catch (Exception ex)
        {
            FailClientControl("Contrôle du compte impossible : " + ex.Message);
        }
        finally
        {
            IsComputerControlBusy = false;
        }
    }

    [RelayCommand]
    private async Task MoveComputerToOuAsync()
    {
        var machine = CurrentClient;
        var context = _connection.Current;
        var target = SelectedTargetOu;
        if (machine is null || context is null || string.IsNullOrWhiteSpace(target) || IsComputerControlBlocked())
        {
            return;
        }

        if (string.Equals(target, machine.Row.OuDistinguishedName, StringComparison.OrdinalIgnoreCase))
        {
            SetStatus(true, "L'ordinateur se trouve déjà dans cette unité d'organisation.");
            return;
        }

        var targetLabel = AvailableOus
            .FirstOrDefault(ou => string.Equals(ou.DistinguishedName, target, StringComparison.OrdinalIgnoreCase))?.Name
            ?? target;
        var confirm = MessageBox.Show(
            "Déplacer l'ordinateur « " + machine.Name + " » vers l'unité d'organisation « " + targetLabel + " » ?\n\n"
            + target + "\n\n"
            + "Le déplacement change les GPO héritées : le poste recevra les stratégies de la nouvelle OU "
            + "au prochain gpupdate. L'opération est auditée.",
            "ADOps GPO Manager — Déplacement AD DS",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        IsComputerControlBusy = true;
        ClientEvents.Add(ConsoleLineViewModel.Command(
            "Move-ADObject -Identity '" + machine.DistinguishedName + "' -TargetPath '" + target + "'"));
        ClientStatusText = "Déplacement de « " + machine.Name + " » vers « " + targetLabel + " »…";
        try
        {
            var result = await _computerControl
                .MoveToOuAsync(context, machine.ToComputerInfo(), target)
                .ConfigureAwait(true);
            if (!result.IsSuccess || result.Value is null)
            {
                FailClientControl(result.Error?.ToDisplayString() ?? "Déplacement impossible.");
                return;
            }

            var message = result.Value;
            await SyncAfterControlAsync().ConfigureAwait(true);
            ClientStatusText = message;
            ClientEvents.Add(ConsoleLineViewModel.Success(message));
            SetStatus(false, message);
        }
        catch (Exception ex)
        {
            FailClientControl("Déplacement impossible : " + ex.Message);
        }
        finally
        {
            IsComputerControlBusy = false;
        }
    }

    [RelayCommand]
    private Task RestartClientComputerAsync() => ExecutePowerAsync(restart: true);

    [RelayCommand]
    private Task ShutdownClientComputerAsync() => ExecutePowerAsync(restart: false);

    private async Task ExecutePowerAsync(bool restart)
    {
        var machine = CurrentClient;
        var context = _connection.Current;
        if (machine is null || context is null || IsComputerControlBlocked())
        {
            return;
        }

        var action = restart ? "Redémarrer" : "Éteindre";
        var confirm = MessageBox.Show(
            action + " le poste « " + machine.Name + " » ?\n\n"
            + (restart
                ? "Le poste redémarrera dans 15 secondes ; les GPO seront réappliquées au démarrage."
                : "Le poste sera éteint dans 15 secondes ; il ne recevra plus ses stratégies tant qu'il restera hors tension.")
            + "\n\nLe travail en cours doit être enregistré. Action auditée, réservée aux postes clients Windows.",
            "ADOps GPO Manager — " + action + " un poste",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        IsComputerControlBusy = true;
        ClientEvents.Add(ConsoleLineViewModel.Command(
            (restart ? "Restart-Computer" : "Stop-Computer") + " — " + machine.Name + " (délai 15 s)"));
        ClientStatusText = action + " de « " + machine.Name + " »…";
        try
        {
            var result = restart
                ? await _computerControl.RestartAsync(context, machine.ToComputerInfo()).ConfigureAwait(true)
                : await _computerControl.ShutdownAsync(context, machine.ToComputerInfo()).ConfigureAwait(true);
            if (!result.IsSuccess || result.Value is null)
            {
                FailClientControl(result.Error?.ToDisplayString() ?? action + " impossible.");
                return;
            }

            ClientStatusText = result.Value;
            ClientEvents.Add(ConsoleLineViewModel.Success(result.Value));
            SetStatus(false, result.Value);
        }
        catch (Exception ex)
        {
            FailClientControl(action + " impossible : " + ex.Message);
        }
        finally
        {
            IsComputerControlBusy = false;
        }
    }

    private bool IsComputerControlBlocked() => IsClientBusy || IsComputerControlBusy;

    private void FailClientControl(string message)
    {
        ClientStatusText = message;
        ClientEvents.Add(ConsoleLineViewModel.Error(message));
        SetStatus(true, message);
    }

    private async Task SyncAfterControlAsync()
    {
        var context = _connection.Current;
        if (context is null)
        {
            return;
        }

        try
        {
            await _sync.SyncAsync(context).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Warning("Synchronisation après contrôle AD DS échouée : " + ex.Message);
        }

        await ReloadAsync().ConfigureAwait(true);
    }

    private void ResetClientConsole()
    {
        ClientRecords.Clear();
        ClientRegistryGroups.Clear();
        ClientGpoResults.Clear();
        ClientEvents.Clear();
        ClientRecordSummary = "Aucune GPO déployée par ADOps sur ce poste.";
        ClientVerifySummary = "Lancez « Vérifier (gpresult) » pour lire les stratégies appliquées sur le poste.";
        OnPropertyChanged(nameof(HasClientRecords));
        OnPropertyChanged(nameof(ClientHasNoRecords));
    }

    private void SetStatus(bool isError, string message)
    {
        IsStatusError = isError;
        StatusMessage = message;
        ShowStatus = true;
    }
}
