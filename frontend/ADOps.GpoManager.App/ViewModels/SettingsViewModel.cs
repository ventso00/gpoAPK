using ADOps.GpoManager.App.Services;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Security;
using ADOps.GpoManager.Core.Services;
using ADOps.GpoManager.Core.Validation;
using ADOps.GpoManager.Infrastructure.Configuration;
using ADOps.GpoManager.Infrastructure.Storage;
using ADOps.GpoManager.PowerShell;
using ADOps.GpoManager.PowerShell.Host;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ADOps.GpoManager.App.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly IConfigurationStore _configurationStore;
    private readonly IAuditService _audit;
    private readonly ILogger _logger;
    private readonly ILauncherService _launcher;
    private readonly StoragePaths _storagePaths;
    private readonly ISessionBootstrapper _bootstrapper;
    private readonly IInventorySyncService _sync;
    private readonly ConnectionState _connection;
    private readonly IPowerShellExecutorFactory _executors;

    [ObservableProperty]
    private bool _autoSyncEnabled;

    [ObservableProperty]
    private int _syncIntervalMinutes = 15;

    [ObservableProperty]
    private int _logMaxFileSizeMb = 5;

    [ObservableProperty]
    private int _logFilesToKeep = 5;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _showStatus;

    [ObservableProperty]
    private bool _isStatusError;

    [ObservableProperty]
    private WinRmTransportMode _winRmTransport = WinRmTransportMode.Auto;

    /// <summary>Contrôleur de domaine cible (FQDN de préférence : Kerberos exige un nom DNS).</summary>
    [ObservableProperty]
    private string _serverFqdn = string.Empty;

    /// <summary>Adresse IP du contrôleur (repli si le FQDN ne se résout pas).</summary>
    [ObservableProperty]
    private string _serverAddress = string.Empty;

    /// <summary>Nom de domaine Active Directory.</summary>
    [ObservableProperty]
    private string _domainFqdn = string.Empty;

    /// <summary>Nom NetBIOS du domaine (facultatif).</summary>
    [ObservableProperty]
    private string _domainNetbios = string.Empty;

    public ConnectionState Connection => _connection;

    /// <summary>Choix de canal WinRM proposés à l'écran (libellé + explication).</summary>
    public IReadOnlyList<WinRmTransportChoice> WinRmTransportChoices { get; } = new[]
    {
        new WinRmTransportChoice(
            WinRmTransportMode.Auto,
            "Négocier (recommandé pour un lab)",
            "Tente HTTPS 5986. Si le contrôleur n'a pas d'écouteur 5986 ou si son certificat "
                + "n'est pas de confiance — cas d'un Windows Server 2019 de test — la commande est "
                + "rejouée en HTTP 5985 et le repli est signalé dans le rapport de santé et le journal."),
        new WinRmTransportChoice(
            WinRmTransportMode.Https,
            "HTTPS strict (5986)",
            "Exige un écouteur HTTPS et un certificat de confiance (autorité, nom, révocation). "
                + "Aucun repli : c'est le mode à utiliser sur un contrôleur de production."),
        new WinRmTransportChoice(
            WinRmTransportMode.Http,
            "HTTP en clair (5985)",
            "Force le canal non chiffré. Réservé à un réseau de test isolé, lorsque HTTPS est impossible."),
    };

    /// <summary>Explication du canal sélectionné, affichée sous la liste.</summary>
    public string WinRmTransportDescription =>
        WinRmTransportChoices.FirstOrDefault(choice => choice.Value == WinRmTransport)?.Description ?? string.Empty;

    /// <summary>Vrai quand le canal sélectionné peut finir en clair sans décision supplémentaire.</summary>
    public bool IsWinRmTransportDowngrade =>
        WinRmTransport is WinRmTransportMode.Http or WinRmTransportMode.Auto;

    public string ConnectionServer => _connection.Current?.ServerFqdn ?? "—";

    public string ConnectionDomain => _connection.Current?.DomainFqdn ?? "Non connecté";

    public string ConnectionMode => _connection.Current is null
        ? "—"
        : _connection.Current.ExecutorMode == ExecutorMode.Local ? "Locale (RSAT)" : "Distante (WinRM)";

    public string ConnectionAccount => _connection.Current?.Credentials.ToDisplayString() ?? "—";

    public string LogDirectory { get; }
    public string DatabaseFile { get; }

    /// <summary>Identité de l'auteur, affichée uniquement dans Paramètres → À propos.</summary>
    public string ApplicationName { get; } = "ADOps GPO Manager";

    public string AuthorName { get; } = "Ventso Rajaomanjato";

    public string AuthorRole { get; } = "Créateur et développeur";

    /// <summary>Version lue dans les métadonnées réelles de l'assembly.</summary>
    public string ApplicationVersion { get; }

    /// <summary>Date du binaire : permet de vérifier quel exécutable est réellement lancé.</summary>
    public string BuildDateText { get; }

    /// <summary>Version du runtime .NET et du système d'exploitation de la machine hôte.</summary>
    public string RuntimeText { get; }

    public string OsText { get; }

    /// <summary>Technologies réellement employées par ce build (aucune dépendance non utilisée n'y figure).</summary>
    public IReadOnlyList<string> TechnologyList { get; } =
    [
        ".NET 10 (net10.0-windows) — moteur et interface",
        "WPF + XAML — interface Fluent, thème piloté par jetons de couleurs",
        "CommunityToolkit.Mvvm 8.4.2 — MVVM (ObservableProperty, RelayCommand)",
        "Microsoft.Extensions.DependencyInjection 10.0.12 — composition racine",
        "PowerShell 5.1 via RSAT (ADModule, GroupPolicy) — lecture et écriture AD DS",
        "WinRM (Invoke-Command, ports 5985/5986) — exécution distante sur les postes",
        "Microsoft.Data.Sqlite 10.0.12 — journal d'audit local",
        "DPAPI (System.Security.Cryptography.ProtectedData 10.0.12) — secrets transitoires",
        "Icônes vectorielles Lucide (géométries XAML intégrées)",
    ];

    public SettingsViewModel(
        IConfigurationStore configurationStore,
        IAuditService audit,
        ILogger logger,
        ILauncherService launcher,
        StoragePaths storagePaths,
        ISessionBootstrapper bootstrapper,
        IInventorySyncService sync,
        ConnectionState connection,
        IPowerShellExecutorFactory executors)
    {
        _configurationStore = configurationStore;
        _audit = audit;
        _logger = logger;
        _launcher = launcher;
        _storagePaths = storagePaths;
        _bootstrapper = bootstrapper;
        _sync = sync;
        _connection = connection;
        _executors = executors;
        LogDirectory = storagePaths.LogDirectory;
        DatabaseFile = storagePaths.DatabaseFile;
        ApplicationVersion = ReadApplicationVersion();
        BuildDateText = ReadBuildDate();
        RuntimeText = RuntimeInformation.FrameworkDescription;
        OsText = RuntimeInformation.OSDescription + " — " + RuntimeInformation.OSArchitecture;
    }

    private static string ReadApplicationVersion()
    {
        var assembly = typeof(SettingsViewModel).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Une hash SourceLink éventuel n'intéresse pas l'écran « À propos ».
            return informational.Split('+')[0];
        }

        return assembly.GetName().Version?.ToString(3) ?? "version inconnue";
    }

    private static string ReadBuildDate()
    {
        try
        {
            var path = typeof(SettingsViewModel).Assembly.Location;
            if (string.IsNullOrEmpty(path))
            {
                // Publié en fichier unique : l'assembly n'a pas d'emplacement disque.
                path = Environment.ProcessPath ?? string.Empty;
            }

            return string.IsNullOrEmpty(path) || !File.Exists(path)
                ? "—"
                : File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm");
        }
        catch (Exception)
        {
            return "—";
        }
    }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        var configuration = _configurationStore.Load();
        AutoSyncEnabled = configuration.AutoSyncEnabled;
        SyncIntervalMinutes = configuration.SyncIntervalMinutes;
        LogMaxFileSizeMb = configuration.LogMaxFileSizeMb;
        LogFilesToKeep = configuration.LogFilesToKeep;
        WinRmTransport = configuration.Security.ResolveWinRmTransport();
        ServerFqdn = configuration.ServerFqdn;
        ServerAddress = configuration.ServerAddress;
        DomainFqdn = configuration.DomainFqdn;
        DomainNetbios = configuration.DomainNetbios ?? string.Empty;
        OnPropertyChanged(nameof(ConnectionServer));
        OnPropertyChanged(nameof(ConnectionDomain));
        OnPropertyChanged(nameof(ConnectionMode));
        OnPropertyChanged(nameof(ConnectionAccount));
    }

    /// <summary>Le canal WinRM est modifié : rafraîchir l'aide contextuelle.</summary>
    partial void OnWinRmTransportChanged(WinRmTransportMode value)
    {
        OnPropertyChanged(nameof(WinRmTransportDescription));
        OnPropertyChanged(nameof(IsWinRmTransportDowngrade));
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (SyncIntervalMinutes is < 1 or > 1440)
        {
            SetStatus(true, "L'intervalle de synchronisation doit être compris entre 1 et 1440 minutes.");
            return;
        }

        if (LogMaxFileSizeMb is < 1 or > 100)
        {
            SetStatus(true, "La taille maximale du journal doit être comprise entre 1 et 100 Mo.");
            return;
        }

        if (LogFilesToKeep is < 1 or > 20)
        {
            SetStatus(true, "Le nombre de journaux conservés doit être compris entre 1 et 20.");
            return;
        }

        // La connexion (serveur, domaine, mode) est enregistrée à la connexion,
        // pas ici : on ne modifie que les paramètres applicatifs.
        var current = _configurationStore.Load();
        var previousTransport = current.Security.ResolveWinRmTransport();

        // Cible du domaine : elle prime sur l'auto-détection. Une adresse IP
        // est acceptée (elle est convertie en FQDN via Netlogon), un nom de
        // domaine doit être un nom DNS complet. Un champ vide conserve la
        // valeur déjà enregistrée (les autres réglages restent sauvegardables).
        var domain = DomainFqdn.Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(domain))
        {
            domain = current.DomainFqdn.Trim().TrimEnd('.');
        }

        var (domainOk, domainError) = DomainValidator.ValidateDomainName(domain);
        if (!domainOk)
        {
            SetStatus(true, domainError ?? "Le nom de domaine est invalide.");
            return;
        }

        var server = ServerFqdn.Trim();
        if (string.IsNullOrWhiteSpace(server))
        {
            server = ServerAddress.Trim();
        }

        if (string.IsNullOrWhiteSpace(server))
        {
            server = current.ServerFqdn.Trim();
        }

        if (!string.IsNullOrWhiteSpace(server))
        {
            var (serverOk, serverError) = DomainValidator.ValidateServerFqdn(server);
            if (!serverOk)
            {
                SetStatus(true, serverError ?? "Le contrôleur de domaine est invalide.");
                return;
            }
        }

        var configuration = current with
        {
            ServerFqdn = server,
            ServerAddress = ServerAddress.Trim(),
            DomainFqdn = domain,
            DomainNetbios = string.IsNullOrWhiteSpace(DomainNetbios) ? null : DomainNetbios.Trim(),
            AutoSyncEnabled = AutoSyncEnabled,
            SyncIntervalMinutes = SyncIntervalMinutes,
            LogMaxFileSizeMb = LogMaxFileSizeMb,
            LogFilesToKeep = LogFilesToKeep,
            // Aligne aussi l'option booléenne héritée pour éviter qu'un ancien
            // « RequireWinRmHttps: false » ne contredise le canal choisi ici.
            Security = current.Security.WithWinRmTransport(WinRmTransport),
        };

        _configurationStore.Save(configuration);

        // Appliqué à chaud : le prochain Health Check utilise déjà le nouveau
        // canal, sans redémarrage ni reconnexion.
        if (_executors is PowerShellExecutorFactory factory)
        {
            factory.Transport = WinRmTransport;
        }

        if (previousTransport != WinRmTransport)
        {
            _logger.Warning(
                "Configuration | canal WinRM modifié : " + previousTransport + " -> " + WinRmTransport
                + (WinRmTransport == WinRmTransportMode.Http ? " (canal en clair)" : string.Empty));

            await _audit.LogAsync(
                AuditActions.SettingsChanged,
                Environment.MachineName,
                WinRmTransport == WinRmTransportMode.Http ? AuditResult.Warning : AuditResult.Information,
                "Canal WinRM modifié : " + previousTransport + " -> " + WinRmTransport,
                WinRmTransport == WinRmTransportMode.Http
                    ? "Le mode distant transmettra les commandes sur un canal non chiffré (port " + WinRmPorts.Http + ")."
                    : null);
        }

        _logger.Information("Configuration enregistrée (sync automatique=" + configuration.AutoSyncEnabled
            + " toutes les " + configuration.SyncIntervalMinutes + " min, logs=" + configuration.LogMaxFileSizeMb + " Mo)");
        await _audit.LogAsync(
            AuditActions.SettingsChanged,
            Environment.MachineName,
            AuditResult.Information,
            "Configuration modifiée",
            "SyncAuto=" + configuration.AutoSyncEnabled + " (" + configuration.SyncIntervalMinutes + " min); Logs=" + configuration.LogMaxFileSizeMb + " Mo; Fichiers conservés=" + configuration.LogFilesToKeep);

        SetStatus(false, WinRmTransport == previousTransport
            ? "Configuration enregistrée. La cible du domaine sera appliquée au prochain « Rejouer la détection de session »."
            : "Configuration enregistrée. Le canal WinRM s'applique au prochain test de connexion.");
    }

    /// <summary>
    /// Rejoue la détection de session (domaine, contrôleur, droits). Utile
    /// après un changement de réseau, une promotion de poste ou l'ajout de
    /// droits : l'écran de connexion ayant été supprimé, c'est la seule façon
    /// de réétablir un contexte de domaine.
    /// </summary>
    [RelayCommand]
    private async Task RedetectSessionAsync()
    {
        IsBusy = true;
        SetStatus(false, "Détection de la session en cours…");

        try
        {
            var result = await _bootstrapper.BootstrapAsync().ConfigureAwait(true);

            if (result.Context is not null)
            {
                _connection.SetConnection(result.Context);
            }

            SetStatus(!result.Success, result.Summary);

            if (result.Warnings.Count > 0)
            {
                _logger.Warning("Détection de session — avertissements : " + string.Join(" | ", result.Warnings));
            }

            RefreshConnectionProperties();

            // Une nouvelle cible n'a de sens que si elle est immédiatement
            // exploitée : la synchronisation est relancée sur le nouveau
            // contrôleur.
            if (result.Success && _connection.Current is not null)
            {
                SetStatus(false, result.Summary + " — synchronisation en cours…");
                var summary = await _sync.SyncAsync(_connection.Current).ConfigureAwait(true);
                _connection.SetSyncResult(summary);
                _connection.LastGpoCount = summary.GposRead;
                SetStatus(
                    summary.State == SyncState.Failed,
                    summary.State == SyncState.Succeeded
                        ? "Nouvelle cible opérationnelle — synchronisation réussie : "
                          + summary.OrganizationalUnitsRead + " OU, "
                          + summary.ComputersRead + " ordinateurs, "
                          + summary.UsersRead + " comptes clients, "
                          + summary.GposRead + " GPO, "
                          + summary.LinksRead + " liens."
                        : "Synchronisation : " + summary.State.ToString().ToLowerInvariant()
                          + (summary.Warnings.Count == 0 ? string.Empty : " — " + string.Join(" | ", summary.Warnings)));
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Détection de session échouée", ex);
            SetStatus(true, "Détection de session impossible : " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshConnectionProperties()
    {
        OnPropertyChanged(nameof(ConnectionServer));
        OnPropertyChanged(nameof(ConnectionDomain));
        OnPropertyChanged(nameof(ConnectionMode));
        OnPropertyChanged(nameof(ConnectionAccount));
    }

    [RelayCommand]
    private void OpenLogFolder() => _launcher.OpenFolder(LogDirectory);

    [RelayCommand]
    private void OpenDataFolder() => _launcher.OpenFolder(_storagePaths.Root);

    private void SetStatus(bool isError, string message)
    {
        IsStatusError = isError;
        StatusMessage = message;
        ShowStatus = true;
    }
}
