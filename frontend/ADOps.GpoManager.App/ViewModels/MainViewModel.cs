using System.Collections.ObjectModel;
using System.Windows.Threading;
using ADOps.GpoManager.App.Controls;
using ADOps.GpoManager.App.Services;
using ADOps.GpoManager.App.ViewModels;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Services;
using ADOps.GpoManager.Infrastructure.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ADOps.GpoManager.App.ViewModels;

public sealed partial class MainViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly IInventorySyncService _sync;
    private readonly IConfigurationStore _configurationStore;
    private readonly ISessionBootstrapper _bootstrapper;
    private readonly DashboardViewModel _dashboard;
    private readonly GpoAssistantViewModel _assistant;
    private readonly WorkspaceViewModel _workspace;
    private readonly AuditViewModel _audit;
    private readonly SettingsViewModel _settings;
    private readonly ILogger _logger;
    private readonly DispatcherTimer _autoSyncTimer;
    private string _activeSection = "workspace";

    private ViewModelBase _currentViewModel = null!;

    public WorkspaceViewModel Workspace => _workspace;
    public AuditViewModel Audit => _audit;
    public SettingsViewModel Settings => _settings;
    public DashboardViewModel Dashboard => _dashboard;

    /// <summary>Supervision : tableau de bord.</summary>
    public ObservableCollection<NavigationItemViewModel> SupervisionNavigation { get; } = new();

    public ObservableCollection<NavigationItemViewModel> PrimaryNavigation { get; } = new();
    public ObservableCollection<NavigationItemViewModel> VerificationNavigation { get; } = new();
    public ObservableCollection<NavigationItemViewModel> ApplicationNavigation { get; } = new();

    public bool IsOverviewSection => _activeSection == "overview";
    public bool IsWorkspaceSection => _activeSection == "workspace";
    public bool IsAuditSection => _activeSection == "audit";
    public bool IsSettingsSection => _activeSection == "settings";

    [ObservableProperty]
    private string _activePageTitle = "Tableau de bord";

    public string CurrentUserInitial
    {
        get
        {
            var name = CurrentUser;
            var shortName = name.Contains('\\') ? name[(name.LastIndexOf('\\') + 1)..] : name;
            return string.IsNullOrWhiteSpace(shortName) ? "A" : shortName[..1].ToUpperInvariant();
        }
    }

    public string CurrentUserShort => CurrentUser.Contains('\\')
        ? CurrentUser[(CurrentUser.LastIndexOf('\\') + 1)..]
        : CurrentUser;

    public string UserDomainText => Connection.Current?.DomainNetbios
        ?? Connection.Current?.DomainFqdn
        ?? "Session locale";

    /// <summary>Page affichée dans la zone principale.</summary>
    public ViewModelBase CurrentViewModel
    {
        get => _currentViewModel;
        set
        {
            if (SetProperty(ref _currentViewModel, value))
            {
                _autoSyncTimer.Stop();
                _autoSyncTimer.Start();
            }
        }
    }

    [ObservableProperty]
    private string _lastSyncText = "Aucune synchronisation";

    /// <summary>Vrai pendant l'exécution d'une synchronisation.</summary>
    [ObservableProperty]
    private bool _isSyncing;

    /// <summary>Message de la dernière synchronisation (visible sous la barre supérieure).</summary>
    [ObservableProperty]
    private string _syncStatusText = string.Empty;

    /// <summary>État de la session implicite détectée au démarrage.</summary>
    [ObservableProperty]
    private string _sessionStatusText = "Détection de la session Windows…";

    /// <summary>Avertissements liés à la session (non-élévation, DC de repli…).</summary>
    [ObservableProperty]
    private string _sessionWarningsText = string.Empty;

    /// <summary>Étapes de la synchronisation en cours (affichées en direct).</summary>
    [ObservableProperty]
    private string _syncProgressText = string.Empty;

    /// <summary>Vrai si la session de domaine a pu être établie.</summary>
    [ObservableProperty]
    private bool _isSessionHealthy;

    public ObservableCollection<SyncStepViewModel> SyncSteps { get; } = new();

    public ConnectionState Connection { get; }

    public string CurrentUser => System.Security.Principal.WindowsIdentity.GetCurrent()?.Name ?? Environment.UserName;

    public MainViewModel(
        INavigationService navigation,
        ConnectionState connection,
        IInventorySyncService sync,
        IConfigurationStore configurationStore,
        ISessionBootstrapper bootstrapper,
        ILogger logger,
        DashboardViewModel dashboard,
        GpoAssistantViewModel assistant,
        WorkspaceViewModel workspace,
        AuditViewModel audit,
        SettingsViewModel settings)
    {
        _navigation = navigation;
        Connection = connection;
        _sync = sync;
        _configurationStore = configurationStore;
        _bootstrapper = bootstrapper;
        _logger = logger;
        _dashboard = dashboard;
        _assistant = assistant;
        _workspace = workspace;
        _audit = audit;
        _settings = settings;

        // Le flux du mode démo (postes → GPO → paramètres → déploiement →
        // console client) est la page d'accueil du produit réel.
        _currentViewModel = workspace;
        _navigation.Navigated += (_, viewModel) => CurrentViewModel = viewModel;
        _workspace.PropertyChanged += OnWorkspacePropertyChanged;

        SupervisionNavigation.Add(new NavigationItemViewModel("overview", "Tableau de bord", LucideIconName.LayoutDashboard));
        PrimaryNavigation.Add(new NavigationItemViewModel("machines", "Comptes utilisateurs ciblés", LucideIconName.Users));
        PrimaryNavigation.Add(new NavigationItemViewModel("gpos", "Gestion des GPO", LucideIconName.ListChecks));
        PrimaryNavigation.Add(new NavigationItemViewModel("params", "Paramètres des GPO", LucideIconName.SlidersHorizontal));
        PrimaryNavigation.Add(new NavigationItemViewModel("deploy", "Appliquer aux postes", LucideIconName.Rocket));
        VerificationNavigation.Add(new NavigationItemViewModel("clients", "Postes clients & console", LucideIconName.Monitor));
        VerificationNavigation.Add(new NavigationItemViewModel("audit", "Journal d'audit", LucideIconName.History));
        ApplicationNavigation.Add(new NavigationItemViewModel("settings", "Paramètres & session", LucideIconName.Settings));

        RefreshNavigation();

        _autoSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _autoSyncTimer.Tick += (_, _) => _ = RunAutoSyncAsync();
    }

    private IEnumerable<NavigationItemViewModel> AllNavigation
        => SupervisionNavigation.Concat(PrimaryNavigation).Concat(VerificationNavigation).Concat(ApplicationNavigation);

    private void OnWorkspacePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceViewModel.ActivePage))
        {
            if (_activeSection == "workspace")
            {
                RefreshNavigation();
            }
        }

        if (e.PropertyName is nameof(WorkspaceViewModel.SelectedGpoCount)
            or nameof(WorkspaceViewModel.MachinesSummaryText))
        {
            UpdateNavigationCounts();
        }
    }

    [RelayCommand]
    private void NavigateTo(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (key is "overview")
        {
            ActivateSection("overview");
            _dashboard.OnNavigatedTo();
            return;
        }

        if (key is "audit")
        {
            ActivateSection("audit");
            _audit.OnNavigatedTo();
            return;
        }

        if (key is "settings")
        {
            ActivateSection("settings");
            _settings.OnNavigatedTo();
            return;
        }

        ActivateSection("workspace");
        _workspace.SetPageCommand.Execute(key);
        RefreshNavigation();
    }

    private void ActivateSection(string section)
    {
        _activeSection = section;
        OnPropertyChanged(nameof(IsOverviewSection));
        OnPropertyChanged(nameof(IsWorkspaceSection));
        OnPropertyChanged(nameof(IsAuditSection));
        OnPropertyChanged(nameof(IsSettingsSection));
        RefreshNavigation();
    }

    private void RefreshNavigation()
    {
        var activeKey = _activeSection == "workspace" ? _workspace.ActivePage : _activeSection;
        foreach (var item in AllNavigation)
        {
            item.IsActive = string.Equals(item.Key, activeKey, StringComparison.OrdinalIgnoreCase);
            if (item.IsActive)
            {
                ActivePageTitle = item.Title;
            }
        }

        UpdateNavigationCounts();
    }

    private void UpdateNavigationCounts()
    {
        var machines = PrimaryNavigation.FirstOrDefault(item => item.Key == "machines");
        var gpos = PrimaryNavigation.FirstOrDefault(item => item.Key == "gpos");
        machines?.SetCount(_workspace.Machines.Count);
        gpos?.SetCount(_workspace.SelectedGpoCount);
    }

    /// <summary>
    /// Séquence de démarrage : détection de la session Windows (qui remplace
    /// l'écran de connexion) puis synchronisation immédiate de l'inventaire.
    /// </summary>
    public async Task InitializeAsync()
    {
        SessionStatusText = "Détection de la session Windows…";
        SessionWarningsText = string.Empty;
        _settings.OnNavigatedTo();

        // Le cache local est affiché immédiatement : le parc reste consultable
        // même si la détection de session ou la synchronisation échoue.
        await Workspace.ReloadAsync().ConfigureAwait(true);

        SessionBootstrapResult result;
        try
        {
            result = await _bootstrapper.BootstrapAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Error("Détection de session échouée", ex);
            IsSessionHealthy = false;
            SessionStatusText = "Détection de session impossible : " + ex.Message;
            SyncStatusText = "La fenêtre reste ouverte : corrigez la cible du domaine dans Paramètres, "
                + "puis relancez la synchronisation.";
            return;
        }

        if (result.Context is not null)
        {
            Connection.SetConnection(result.Context);
        }

        // Adresse IP du contrôleur : affichée dans le tableau de bord pour
        // que l'administrateur voie immédiatement quelle machine est ciblée.
        Connection.ServerAddress = _configurationStore.Load().ServerAddress;

        IsSessionHealthy = result.Success;
        SessionStatusText = result.Summary;
        SessionWarningsText = string.Join("  |  ", result.Warnings);
        Connection.HealthSummary = result.Report?.Summary;

        if (!result.Success)
        {
            SyncStatusText = result.Error ?? "Aucune session de domaine : synchronisation impossible.";
            return;
        }

        await SyncNowAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Synchronisation manuelle : collecte les OU, les ordinateurs, les comptes
    /// clients, les GPO et leurs liens, puis met à jour le cache local.
    /// </summary>
    [RelayCommand]
    private async Task SyncNowAsync()
    {
        if (IsSyncing || Connection.IsSyncInProgress)
        {
            return;
        }

        var context = Connection.Current;
        if (context is null)
        {
            SyncStatusText = "Aucune session de domaine active : la synchronisation est impossible.";
            return;
        }

        IsSyncing = true;
        Connection.IsSyncInProgress = true;
        SyncStatusText = "Synchronisation en cours…";
        SyncSteps.Clear();
        SyncProgressText = string.Empty;

        // Progress<T> capture le contexte de synchronisation : chaque étape est
        // donc répercutée sur le thread d'interface, sans toucher à l'UI
        // depuis un thread secondaire.
        var progress = new Progress<SyncStepResult>(OnSyncStep);

        try
        {
            var summary = await _sync.SyncAsync(context, progress).ConfigureAwait(true);
            Connection.SetSyncResult(summary);
            Connection.LastGpoCount = summary.GposRead;
            UpdateLastSyncText();

            SyncStatusText = summary.State switch
            {
                SyncState.Succeeded => "Synchronisation réussie : "
                    + summary.OrganizationalUnitsRead + " OU, "
                    + summary.ComputersRead + " ordinateurs, "
                    + summary.UsersRead + " comptes clients, "
                    + summary.GposRead + " GPO, "
                    + summary.LinksRead + " liens.",
                SyncState.Partial => "Synchronisation partielle : "
                    + string.Join(" | ", summary.Warnings),
                SyncState.Failed => "Synchronisation échouée : " + (summary.Error ?? "cause inconnue"),
                _ => "Synchronisation terminée.",
            };

            await _dashboard.RefreshAsync().ConfigureAwait(true);
            await Workspace.ReloadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Error("Synchronisation manuelle échouée", ex);
            Connection.MarkSyncFailed(ex.Message);
            UpdateLastSyncText();
            SyncStatusText = "Erreur de synchronisation : " + ex.Message;
        }
        finally
        {
            IsSyncing = false;
            Connection.IsSyncInProgress = false;
        }
    }

    /// <summary>Met à jour l'étape concernée dans la liste de progression.</summary>
    private void OnSyncStep(SyncStepResult result)
    {
        var step = SyncSteps.FirstOrDefault(candidate => candidate.Name == result.Name);
        if (step is null)
        {
            step = new SyncStepViewModel(result.Name);
            SyncSteps.Add(step);
        }

        step.Apply(result);
        SyncProgressText = result.Name + " : " + result.StateText
            + (string.IsNullOrWhiteSpace(result.Message) ? string.Empty : " — " + result.Message);
    }

    /// <summary>Synchronisation automatique configurable (période).</summary>
    private async Task RunAutoSyncAsync()
    {
        if (!Connection.IsConnected || Connection.Current is null)
        {
            return;
        }

        if (IsSyncing)
        {
            return;
        }

        var configuration = _configurationStore.Load();
        if (!configuration.AutoSyncEnabled)
        {
            return;
        }

        if (Connection.LastSyncUtc is not null
            && (DateTimeOffset.UtcNow - Connection.LastSyncUtc.Value).TotalMinutes < configuration.SyncIntervalMinutes)
        {
            return;
        }

        try
        {
            var summary = await _sync.SyncAsync(Connection.Current).ConfigureAwait(true);
            Connection.SetSyncResult(summary);
            Connection.LastGpoCount = summary.GposRead;
            UpdateLastSyncText();
            await Workspace.ReloadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Error("Synchronisation automatique échouée", ex);
        }
    }

    internal void UpdateLastSyncText()
    {
        LastSyncText = Connection.LastSyncUtc is null
            ? "Aucune synchronisation"
            : "Dernière synchro : " + Connection.LastSyncUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
              + " (" + (Connection.LastSyncSummary ?? string.Empty) + ")";
    }
}
