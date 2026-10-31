using System.Collections.ObjectModel;
using ADOps.GpoManager.App.Services;
using ADOps.GpoManager.Core.Commands;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Services;
using ADOps.GpoManager.Infrastructure.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ADOps.GpoManager.App.ViewModels;

/// <summary>
/// Assistant de déploiement des GPO — parcours unique en quatre étapes :
/// <list type="number">
/// <item><description><b>Cibles</b> : tous les postes et comptes clients du domaine,
/// avec filtres, sélection multiple et gestion locale (ajout / modification / retrait).</description></item>
/// <item><description><b>GPO</b> : toutes les GPO du domaine, les modèles du catalogue
/// et l'option « Ajouter une GPO » pour créer celle dont on a besoin.</description></item>
/// <item><description><b>Vérification</b> : plan d'impact, liens à créer, filtre de
/// sécurité et rafraîchissement des postes.</description></item>
/// <item><description><b>Exécution</b> : terminal en direct, résultat par opération et
/// liste des comptes concernés.</description></item>
/// </list>
/// </summary>
public sealed partial class GpoAssistantViewModel : ViewModelBase
{
    private readonly IGpoService _gpoService;
    private readonly IGpoCatalogService _catalog;
    private readonly IGpoDeploymentService _deployment;
    private readonly IGpoTemplateDeploymentService _templateDeployment;
    private readonly IInventorySyncService _sync;
    private readonly InventoryQueryRepository _inventory;
    private readonly AssistantTargetRepository _targets;
    private readonly ConnectionState _connection;
    private readonly ILogger _logger;

    private MachineDeploymentPlan? _plan;

    /// <summary>Liste complète des comptes (filtrage appliqué sur la vue).</summary>
    private readonly List<TargetAccountRow> _allAccounts = new();

    /// <summary>Liste complète des postes (filtrage appliqué sur la vue).</summary>
    private readonly List<TargetComputerRow> _allComputers = new();

    /// <summary>Liste complète des stratégies (filtrage appliqué sur la vue).</summary>
    private readonly List<GpoChoice> _allGpoChoices = new();

    [ObservableProperty]
    private int _currentStep = 1;

    [ObservableProperty]
    private string _targetSearch = string.Empty;

    [ObservableProperty]
    private string _targetStatusText = string.Empty;

    [ObservableProperty]
    private bool _showOnlySelected;

    [ObservableProperty]
    private string _gpoSearch = string.Empty;

    [ObservableProperty]
    private string? _selectedCategory;

    [ObservableProperty]
    private string _planSummary = "Sélectionnez des cibles et des GPO pour établir le plan.";

    [ObservableProperty]
    private bool _applySecurityFilter = true;

    [ObservableProperty]
    private bool _refreshTargets = true;

    [ObservableProperty]
    private string _securityGroupDn = string.Empty;

    [ObservableProperty]
    private string _resultSummary = string.Empty;

    [ObservableProperty]
    private string _executionStatusText = string.Empty;

    [ObservableProperty]
    private bool _hasPlan;

    [ObservableProperty]
    private bool _isExecuting;

    /// <summary>Message de l'étape courante (aide contextuelle).</summary>
    [ObservableProperty]
    private string _stepHint = string.Empty;

    public ObservableCollection<TargetAccountRow> Accounts { get; } = new();

    public ObservableCollection<TargetComputerRow> Computers { get; } = new();

    public ObservableCollection<GpoChoice> GpoChoices { get; } = new();

    public ObservableCollection<ExecutionLine> Console { get; } = new();

    public ObservableCollection<AssistantTargetEntry> AppliedAccounts { get; } = new();

    public ObservableCollection<string> Categories { get; } = new();

    public ConnectionState Connection => _connection;

    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;
    public bool IsStep3 => CurrentStep == 3;
    public bool IsStep4 => CurrentStep == 4;

    public string StepTitle => CurrentStep switch
    {
        1 => "Étape 1 — Cibles à couvrir",
        2 => "Étape 2 — Stratégies à appliquer",
        3 => "Étape 3 — Vérification",
        _ => "Étape 4 — Exécution",
    };

    public int SelectedAccountCount => Accounts.Count(row => row.IsSelected);
    public int SelectedComputerCount => Computers.Count(row => row.IsSelected);
    public int SelectedGpoCount => GpoChoices.Count(choice => choice.IsSelected);

    public GpoAssistantViewModel(
        IGpoService gpoService,
        IGpoCatalogService catalog,
        IGpoDeploymentService deployment,
        IGpoTemplateDeploymentService templateDeployment,
        IInventorySyncService sync,
        InventoryQueryRepository inventory,
        AssistantTargetRepository targets,
        ConnectionState connection,
        ILogger logger)
    {
        _gpoService = gpoService;
        _catalog = catalog;
        _deployment = deployment;
        _templateDeployment = templateDeployment;
        _sync = sync;
        _inventory = inventory;
        _targets = targets;
        _connection = connection;
        _logger = logger;
        UpdateStepHint();
    }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        _ = LoadAsync();
    }

    partial void OnCurrentStepChanged(int value)
    {
        UpdateStepHint();

        // Les propriétés d'étape ne sont pas des champs observables : leur
        // changement doit être notifié explicitement, sinon les quatre
        // panneaux resteraient superposés.
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
        OnPropertyChanged(nameof(IsStep3));
        OnPropertyChanged(nameof(IsStep4));
        OnPropertyChanged(nameof(StepTitle));
    }

    partial void OnTargetSearchChanged(string value) => ApplyTargetFilter();

    partial void OnShowOnlySelectedChanged(bool value) => ApplyTargetFilter();

    partial void OnGpoSearchChanged(string value) => ApplyGpoFilter();

    partial void OnSelectedCategoryChanged(string? value) => ApplyGpoFilter();

    private void UpdateStepHint()
    {
        StepHint = CurrentStep switch
        {
            1 => "Cochez les postes et les comptes clients à couvrir, puis « Suivant ». Vous pouvez aussi ajouter une cible manuellement.",
            2 => "Choisissez une ou plusieurs stratégies : GPO existantes du domaine ou modèles du catalogue. « Ajouter une GPO » en crée une nouvelle dans le domaine.",
            3 => "Contrôlez le plan : liens à créer, comptes et postes concernés, filtre de sécurité et rafraîchissement.",
            _ => "L'application s'exécute. Le terminal retrace chaque opération ; la liste des comptes concernés est disponible en fin d'exécution.",
        };
    }

    // ─────────────────────────── Chargement ───────────────────────────

    private async Task LoadAsync()
    {
        if (!Connection.IsConnected || Connection.Current is null)
        {
            TargetStatusText = "Aucune session de domaine : synchronisez d'abord l'environnement.";
            return;
        }

        IsBusy = true;
        try
        {
            await LoadTargetsAsync();
            await LoadGposAsync();
        }
        catch (Exception ex)
        {
            _logger.Error("Chargement de l'assistant échoué", ex);
            TargetStatusText = "Erreur de chargement : " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Charge les comptes clients et les postes (cache local, synchronisé si vide).</summary>
    private async Task LoadTargetsAsync()
    {
        var accounts = await _inventory.GetClientAccountsAsync();
        if (accounts.Count == 0)
        {
            var summary = await _sync.SyncAsync(Connection.Current!);
            Connection.SetSyncResult(summary);
            accounts = await _inventory.GetClientAccountsAsync();
        }

        var computers = await _inventory.GetComputersAsync();
        var local = await _targets.GetAllAsync();

        var selectedAccounts = local
            .Where(target => target.Kind == AssistantTargetRepository.KindAccount)
            .ToDictionary(target => target.DistinguishedName, StringComparer.OrdinalIgnoreCase);
        var selectedComputers = local
            .Where(target => target.Kind == AssistantTargetRepository.KindComputer)
            .ToDictionary(target => target.DistinguishedName, StringComparer.OrdinalIgnoreCase);

        Accounts.Clear();
        _allAccounts.Clear();
        foreach (var account in accounts)
        {
            var row = new TargetAccountRow
            {
                DistinguishedName = account.DistinguishedName,
                SamAccountName = account.SamAccountName,
                DisplayName = string.IsNullOrWhiteSpace(account.DisplayName) ? account.SamAccountName : account.DisplayName,
                Department = account.Department,
                OuName = account.OuName,
                OuDistinguishedName = account.OuDistinguishedName,
                IsAdministrator = account.IsAdministrator,
                Enabled = account.Enabled,
                AccountLocked = account.AccountLocked,
            };

            if (selectedAccounts.TryGetValue(account.DistinguishedName, out var target))
            {
                row.IsSelected = target.IsSelected;
                row.Note = target.Note;
            }
            else
            {
                // Par défaut, les comptes standards du domaine sont proposés.
                row.IsSelected = !account.IsAdministrator;
            }

            _allAccounts.Add(row);
        }

        Computers.Clear();
        _allComputers.Clear();
        foreach (var computer in computers)
        {
            var row = new TargetComputerRow
            {
                DistinguishedName = computer.DistinguishedName,
                Name = computer.Name,
                OuName = computer.OuName,
                OuDistinguishedName = computer.OuDistinguishedName,
                OperatingSystem = computer.OperatingSystem,
                Enabled = computer.Enabled,
            };

            if (selectedComputers.TryGetValue(computer.DistinguishedName, out var target))
            {
                row.IsSelected = target.IsSelected;
                row.Note = target.Note;
            }

            _allComputers.Add(row);
        }

        ApplyTargetFilter();
    }

    /// <summary>Charge les GPO du domaine et les modèles du catalogue.</summary>
    private async Task LoadGposAsync()
    {
        var result = await _gpoService.ListGposAsync(Connection.Current!);
        GpoChoices.Clear();
        _allGpoChoices.Clear();

        if (result.IsSuccess && result.Value is not null)
        {
            foreach (var gpo in result.Value)
            {
                _allGpoChoices.Add(new GpoChoice
                {
                    ExistsInDomain = true,
                    Id = gpo.Id,
                    DisplayName = gpo.DisplayName,
                    Description = gpo.Description,
                    Category = "Domaine",
                    RiskLevel = "Low",
                    StatusText = gpo.StatusText,
                });
            }
        }

        foreach (var template in _catalog.GetTemplates())
        {
            _allGpoChoices.Add(new GpoChoice
            {
                ExistsInDomain = false,
                TemplateId = template.TemplateId,
                DisplayName = template.DisplayName,
                Description = template.Description,
                Category = template.Category,
                RiskLevel = template.RiskLevel,
                ScopeText = template.Scope == PolicyScope.User ? "Utilisateur" : "Ordinateur",
            });
        }

        Categories.Clear();
        Categories.Add("Toutes");
        foreach (var category in _allGpoChoices.Select(choice => choice.Category).Distinct().OrderBy(category => category))
        {
            Categories.Add(category);
        }

        ApplyGpoFilter();
    }

    private void ApplyTargetFilter()
    {
        var search = TargetSearch.Trim();

        Accounts.Clear();
        foreach (var row in _allAccounts.Where(row => Matches(row, search, ShowOnlySelected)))
        {
            Accounts.Add(row);
        }

        Computers.Clear();
        foreach (var row in _allComputers.Where(row => Matches(row, search, ShowOnlySelected)))
        {
            Computers.Add(row);
        }

        TargetStatusText = Accounts.Count + " compte(s) client(s) affiché(s) · "
            + Computers.Count + " poste(s) affiché(s) sur "
            + _allAccounts.Count + " comptes et " + _allComputers.Count + " postes du domaine";

        UpdateSelectionSummary();
    }

    private static bool Matches(TargetAccountRow row, string search, bool onlySelected)
    {
        if (onlySelected && !row.IsSelected)
        {
            return false;
        }

        if (search.Length == 0)
        {
            return true;
        }

        return row.SamAccountName.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
            || (row.Department?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static bool Matches(TargetComputerRow row, string search, bool onlySelected)
    {
        if (onlySelected && !row.IsSelected)
        {
            return false;
        }

        if (search.Length == 0)
        {
            return true;
        }

        return row.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || (row.OuName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void ApplyGpoFilter()
    {
        var search = GpoSearch.Trim();
        var category = SelectedCategory;

        GpoChoices.Clear();
        foreach (var choice in _allGpoChoices.Where(choice =>
            (category is null || category.Length == 0 || category == "Toutes" || choice.Category == category)
            && (search.Length == 0
                || choice.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (choice.Description?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))))
        {
            GpoChoices.Add(choice);
        }

        OnPropertyChanged(nameof(SelectedGpoCount));
    }

    private void UpdateSelectionSummary()
    {
        OnPropertyChanged(nameof(SelectedAccountCount));
        OnPropertyChanged(nameof(SelectedComputerCount));
        OnPropertyChanged(nameof(SelectedGpoCount));
    }

    // ─────────────────────────── Sélection ───────────────────────────

    [RelayCommand]
    private void SelectAllAccounts()
    {
        foreach (var row in Accounts)
        {
            row.IsSelected = true;
        }

        UpdateSelectionSummary();
    }

    [RelayCommand]
    private void SelectAllComputers()
    {
        foreach (var row in Computers)
        {
            row.IsSelected = true;
        }

        UpdateSelectionSummary();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var row in Accounts)
        {
            row.IsSelected = false;
        }

        foreach (var row in Computers)
        {
            row.IsSelected = false;
        }

        UpdateSelectionSummary();
    }

    /// <summary>Ajoute une cible manuelle (CRUD local, sans toucher à AD DS).</summary>
    [RelayCommand]
    private async Task AddTargetAsync()
    {
        var name = TargetSearch.Trim();
        if (name.Length == 0)
        {
            TargetStatusText = "Saisissez le nom de la cible à ajouter.";
            return;
        }

        var distinguishedName = "CN=" + name + ",OU=Manual,DC=" + (Connection.Current?.DomainFqdn ?? "local");

        var row = new TargetAccountRow
        {
            DistinguishedName = distinguishedName,
            SamAccountName = name,
            DisplayName = name,
            IsSelected = true,
        };

        Accounts.Add(row);
        _allAccounts.Add(row);
        TargetSearch = string.Empty;

        await _targets.SaveAsync(new AssistantTarget
        {
            Kind = AssistantTargetRepository.KindAccount,
            DistinguishedName = distinguishedName,
            DisplayName = name,
            SamAccountName = name,
            IsSelected = true,
        }).ConfigureAwait(true);

        UpdateSelectionSummary();
        TargetStatusText = "Cible ajoutée : " + name;
    }

    /// <summary>Retire une cible du périmètre (CRUD local).</summary>
    [RelayCommand]
    private async Task RemoveTargetAsync(TargetAccountRow row)
    {
        if (row is null)
        {
            return;
        }

        Accounts.Remove(row);
        _allAccounts.Remove(row);
        await _targets.DeleteAsync(AssistantTargetRepository.KindAccount, row.DistinguishedName).ConfigureAwait(true);
        UpdateSelectionSummary();
    }

    /// <summary>Supprime toutes les cibles locales ajoutées manuellement.</summary>
    [RelayCommand]
    private async Task ResetTargetsAsync()
    {
        Accounts.Clear();
        Computers.Clear();
        _allAccounts.Clear();
        _allComputers.Clear();
        await _targets.DeleteAllAsync(AssistantTargetRepository.KindAccount).ConfigureAwait(true);
        await _targets.DeleteAllAsync(AssistantTargetRepository.KindComputer).ConfigureAwait(true);
        UpdateSelectionSummary();
        TargetStatusText = "Cibles réinitialisées.";
    }

    // ─────────────────────────── Navigation ───────────────────────────

    [RelayCommand]
    private void NextStep()
    {
        if (CurrentStep == 2)
        {
            // Le plan doit être établi avant de passer à la vérification.
            _ = BuildPlanAndAdvanceAsync();
            return;
        }

        if (CurrentStep < 4)
        {
            CurrentStep++;
        }
    }

    private async Task BuildPlanAndAdvanceAsync()
    {
        await BuildPlanAsync();
        if (HasPlan)
        {
            CurrentStep = 3;
        }
    }

    [RelayCommand]
    private void PreviousStep()
    {
        if (CurrentStep > 1)
        {
            CurrentStep--;
        }
    }

    /// <summary>
    /// Saut direct à une étape. Le paramètre est une chaîne : c'est le format
    /// que produit « CommandParameter » en XAML.
    /// </summary>
    [RelayCommand]
    private void GoToStep(string step)
    {
        if (int.TryParse(step, out var value))
        {
            CurrentStep = Math.Clamp(value, 1, 4);
        }
    }

    // ─────────────────────────── Plan ───────────────────────────

    private async Task BuildPlanAsync()
    {
        var context = Connection.Current;
        if (context is null)
        {
            PlanSummary = "Aucune session de domaine : impossible d'établir un plan.";
            HasPlan = false;
            return;
        }

        IsBusy = true;
        try
        {
            var selectedDomainGpos = GpoChoices
                .Where(choice => choice.IsSelected && choice.ExistsInDomain)
                .Select(choice => choice.ToSummary())
                .ToList();

            // Les modèles du catalogue ne sont pas encore des GPO du domaine :
            // ils sont matérialisés au moment de l'exécution. Le plan doit donc
            // les accepter explicitement, sinon l'assistant reste bloqué à
            // l'étape 2 dès qu'aucune GPO existante n'est cochée.
            var selectedTemplates = GpoChoices
                .Where(choice => choice.IsSelected && !string.IsNullOrWhiteSpace(choice.TemplateId))
                .ToList();

            var selectedAccounts = Accounts
                .Where(row => row.IsSelected && row.Enabled && !row.IsAdministrator)
                .Select(row => row.ToUserInfo())
                .ToList();
            var selectedComputers = Computers.Where(row => row.IsSelected).Select(row => row.ToComputerInfo()).ToList();

            if (selectedDomainGpos.Count == 0 && selectedTemplates.Count == 0)
            {
                PlanSummary = "Sélectionnez au moins une GPO ou un modèle à appliquer.";
                HasPlan = false;
                _plan = null;
                return;
            }

            if (selectedAccounts.Count == 0 && selectedComputers.Count == 0)
            {
                PlanSummary = "Sélectionnez au moins un compte standard ou un poste client dans l'étape 1.";
                HasPlan = false;
                _plan = null;
                return;
            }

            // Conteneurs de destination : OU des comptes et des postes sélectionnés.
            // À défaut, la racine du domaine sert d'ancrage (les conteneurs CN= ne
            // supportent pas les liens GPO).
            var containers = selectedAccounts
                .Select(account => account.OuDistinguishedName)
                .Concat(selectedComputers.Select(computer => computer.OuDistinguishedName))
                .Where(dn => !string.IsNullOrWhiteSpace(dn))
                .Select(dn => dn!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (containers.Count == 0)
            {
                containers.Add(BuildDomainDistinguishedName(context.DomainFqdn));
            }

            var entries = new List<MachineDeploymentEntry>();

            // Les GPO déjà présentes dans le domaine passent par le planificateur existant.
            if (selectedDomainGpos.Count > 0)
            {
                var links = await _inventory.GetAllLinksAsync();
                var plan = await _deployment.PlanMachineDeploymentAsync(
                    context,
                    selectedDomainGpos,
                    selectedComputers,
                    selectedAccounts,
                    links,
                    ApplySecurityFilter ? SecurityGroupDn : null).ConfigureAwait(true);

                if (plan.IsSuccess && plan.Value is not null)
                {
                    entries.AddRange(plan.Value.Entries);
                }
            }

            // Un modèle = une GPO matérialisée par conteneur de destination.
            foreach (var template in selectedTemplates)
            {
                foreach (var container in containers)
                {
                    entries.Add(new MachineDeploymentEntry
                    {
                        GpoName = template.DisplayName,
                        TargetDistinguishedName = container,
                        TargetName = DisplayOu(container),
                        AccountNames = selectedAccounts.Select(account => account.SamAccountName).ToList(),
                        ComputerNames = selectedComputers.Select(computer => computer.Name).ToList(),
                    });
                }
            }

            _plan = new MachineDeploymentPlan
            {
                Entries = entries,
                TargetAccounts = selectedAccounts,
                TargetComputers = selectedComputers,
                Warnings = Array.Empty<string>(),
                Errors = Array.Empty<string>(),
            };

            HasPlan = _plan.IsValid;
            PlanSummary = _plan.PlannedLinkCount + " lien(s) de GPO à créer · "
                + _plan.AffectedComputerCount + " poste(s) · "
                + _plan.AffectedAccountCount + " compte(s) client"
                + (selectedTemplates.Count > 0
                    ? " · " + selectedTemplates.Count + " modèle(s) du catalogue (matérialisés à l'exécution)"
                    : string.Empty);
        }
        catch (Exception ex)
        {
            _logger.Error("Établissement du plan échoué", ex);
            PlanSummary = "Erreur : " + ex.Message;
            HasPlan = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Affiche le nom lisible d'une OU à partir de son DistinguishedName.</summary>
    private static string DisplayOu(string distinguishedName)
    {
        var first = distinguishedName.Split(',', 2)[0];
        var index = first.IndexOf('=');
        return index >= 0 && index + 1 < first.Length ? first[(index + 1)..] : distinguishedName;
    }

    /// <summary>Construit le DN du domaine à partir de son FQDN (ex: DC=sarytany,DC=local).</summary>
    private static string BuildDomainDistinguishedName(string domainFqdn)
    {
        if (string.IsNullOrWhiteSpace(domainFqdn))
        {
            return "DC=local";
        }

        var parts = domainFqdn.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0
            ? "DC=" + domainFqdn
            : string.Join(",", parts.Reverse().Select(part => "DC=" + part));
    }

    // ─────────────────────────── Exécution ───────────────────────────

    [RelayCommand]
    private async Task ExecuteAsync()
    {
        var context = Connection.Current;
        if (context is null)
        {
            ExecutionStatusText = "Aucune session de domaine active : synchronisez d'abord l'environnement.";
            return;
        }

        if (IsExecuting)
        {
            return;
        }

        // Le déploiement réel est porté par les modèles du catalogue : une GPO
        // existante du domaine n'est pas matérialisée par ce parcours.
        var selectedGpoSelections = GpoChoices
            .Where(g => g.IsSelected && !string.IsNullOrWhiteSpace(g.TemplateId))
            .Select(g => new TemplateDeploymentSelection
            {
                TemplateId = g.TemplateId!,
                Parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
                GpoName = g.DisplayName,
            })
            .ToList();

        if (selectedGpoSelections.Count == 0)
        {
            ExecutionStatusText = "Sélectionnez au moins un modèle du catalogue à déployer.";
            return;
        }

        var selectedUsers = Accounts
            .Where(a => a.IsSelected && a.Enabled && !a.IsAdministrator)
            .Select(a => a.ToUserInfo())
            .ToList();

        if (selectedUsers.Count == 0)
        {
            ExecutionStatusText = "Sélectionnez au moins un compte standard activé (les administrateurs sont exclus).";
            return;
        }

        IsExecuting = true;
        Console.Clear();
        AppliedAccounts.Clear();
        ExecutionStatusText = "Exécution en cours…";
        ResultSummary = string.Empty;

        var progress = new Progress<ExecutionLine>(line =>
        {
            Console.Add(line);
            ExecutionStatusText = line.Message;
        });

        try
        {
            // ── Synchronisation AD DS obligatoire avant tout déploiement ──
            // La source de vérité est le contrôleur de domaine (ventso.sarytany.local) :
            // on relit la liste complète des utilisateurs actifs, des machines du
            // domaine et de la structure des OU, puis on rafraîchit le cache local
            // avant la matérialisation et la liaison des GPO. Cela évite toute cible
            // absente ou obsolète (cache vide / périmé) qui aboutirait à un « 0 poste
            // sélectionné ».
            ExecutionStatusText = "Synchronisation AD DS avant déploiement… (ventso.sarytany.local)";
            var syncSummary = await _sync.SyncAsync(context).ConfigureAwait(true);
            Connection.SetSyncResult(syncSummary);
            await LoadTargetsAsync().ConfigureAwait(true);
            ExecutionStatusText = "Synchronisation AD DS terminée — déploiement en cours…";

            var result = await _templateDeployment.DeployAsync(
                context,
                selectedGpoSelections,
                selectedUsers,
                ApplySecurityFilter,
                progress).ConfigureAwait(true);

            if (!result.IsSuccess || result.Value is null)
            {
                Console.Add(ExecutionLine.Error(result.Error?.ToDisplayString() ?? "Déploiement impossible."));
                ExecutionStatusText = "Échec du déploiement.";
                return;
            }

            var deployment = result.Value;
            ResultSummary = deployment.SummaryText
                + (deployment.FailedComputers.Count > 0
                    ? " — " + deployment.FailedComputers.Count + " poste(s) en échec"
                    : string.Empty);

            foreach (var record in deployment.Records)
            {
                AppliedAccounts.Add(new AssistantTargetEntry
                {
                    Kind = record.ComputerName == "(postes clients)" ? "Postes" : "Compte",
                    Name = record.GpoName,
                    Detail = record.FilterGroup ?? string.Empty,
                    Secondary = record.OuDistinguishedName ?? string.Empty,
                    Status = "Standard",
                    DistinguishedName = record.ComputerDistinguishedName,
                });
            }

            ExecutionStatusText = "Déploiement terminé.";
        }
        catch (Exception ex)
        {
            _logger.Error("Déploiement échoué", ex);
            Console.Add(ExecutionLine.Error(ex.Message));
            ExecutionStatusText = "Erreur : " + ex.Message;
        }
        finally
        {
            IsExecuting = false;
        }
    }

    // ─────────────────────────── Ajout de GPO ───────────────────────────

    [ObservableProperty]
    private bool _showCreateGpo;

    [ObservableProperty]
    private string _newGpoName = string.Empty;

    [ObservableProperty]
    private string _newGpoDescription = string.Empty;

    [ObservableProperty]
    private string _newGpoRegistryKey = string.Empty;

    [ObservableProperty]
    private string _newGpoValueName = string.Empty;

    [ObservableProperty]
    private string _newGpoValue = string.Empty;

    [ObservableProperty]
    private string _newGpoValueType = "DWord";

    /// <summary>Types de valeur proposés.</summary>
    public IReadOnlyList<string> ValueTypes { get; } = new[] { "DWord", "String", "ExpandString", "MultiString", "QWord" };

    /// <summary>Types de valeur proposés (vue).</summary>
    public IReadOnlyList<string> ValueTypesView => ValueTypes;

    [RelayCommand]
    private void OpenCreateGpo()
    {
        ShowCreateGpo = true;
        NewGpoName = string.Empty;
        NewGpoDescription = string.Empty;
        NewGpoRegistryKey = string.Empty;
        NewGpoValueName = string.Empty;
        NewGpoValue = string.Empty;
    }

    [RelayCommand]
    private void CloseCreateGpo() => ShowCreateGpo = false;

    /// <summary>Crée réellement la GPO dans le domaine.</summary>
    [RelayCommand]
    private async Task CreateGpoAsync()
    {
        var context = Connection.Current;
        if (context is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(NewGpoName))
        {
            ExecutionStatusText = "Le nom de la GPO est requis.";
            return;
        }

        IsBusy = true;
        try
        {
            var settings = new List<GpoRegistrySetting>();
            if (!string.IsNullOrWhiteSpace(NewGpoRegistryKey) || !string.IsNullOrWhiteSpace(NewGpoValueName))
            {
                settings.Add(new GpoRegistrySetting
                {
                    Key = NewGpoRegistryKey.Trim(),
                    ValueName = NewGpoValueName.Trim(),
                    ValueType = NewGpoValueType,
                    Value = NewGpoValue.Trim(),
                });
            }

            var result = await _deployment.CreateGpoAsync(context, NewGpoName, settings, NewGpoDescription).ConfigureAwait(true);
            if (!result.IsSuccess || result.Value is null)
            {
                ExecutionStatusText = result.Error?.ToDisplayString() ?? "Création impossible.";
                return;
            }

            GpoChoices.Add(new GpoChoice
            {
                ExistsInDomain = true,
                Id = result.Value.Id,
                DisplayName = result.Value.DisplayName,
                Description = result.Value.Description,
                Category = "Domaine",
                IsSelected = true,
                StatusText = "Créée",
            });

            ShowCreateGpo = false;
            ExecutionStatusText = "GPO « " + result.Value.DisplayName + " » créée dans le domaine.";
            UpdateSelectionSummary();
        }
        catch (Exception ex)
        {
            _logger.Error("Création de GPO échouée", ex);
            ExecutionStatusText = "Erreur : " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>Ligne de la table « comptes concernés » affichée après l'exécution.</summary>
public sealed class AssistantTargetEntry
{
    public string Kind { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public string Secondary { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string DistinguishedName { get; init; } = string.Empty;
}
