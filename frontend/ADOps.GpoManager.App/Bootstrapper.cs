using Microsoft.Extensions.DependencyInjection;
using ADOps.GpoManager.ActiveDirectory;
using ADOps.GpoManager.App.Controls;
using ADOps.GpoManager.App.Services;
using ADOps.GpoManager.App.ViewModels;
using ADOps.GpoManager.App.Views;
using ADOps.GpoManager.Core.Diagnostics;
using ADOps.GpoManager.Core.Security;
using ADOps.GpoManager.Core.Services;
using ADOps.GpoManager.Infrastructure.Configuration;
using ADOps.GpoManager.Infrastructure.Logging;
using ADOps.GpoManager.Infrastructure.Services;
using ADOps.GpoManager.Infrastructure.Storage;
using ADOps.GpoManager.PowerShell;
using ADOps.GpoManager.PowerShell.Host;
using ADOps.GpoManager.PowerShell.Services;
using ADOps.GpoManager.Templates;

namespace ADOps.GpoManager.App;

/// <summary>Composition racine de l'application (injection de dépendances).</summary>
public static class Bootstrapper
{
    public static IServiceProvider ConfigureServices()
    {
        var storagePaths = StoragePaths.Default;
        var configurationStore = new JsonConfigurationStore(storagePaths);
        var configuration = configurationStore.Load();

        // Le fichier de configuration est le panneau de configuration lisible et
        // éditable de la cible du domaine (contrôleur, adresse IP, domaine) :
        // il doit donc exister dès le premier démarrage.
        if (!configurationStore.FileExists)
        {
            configurationStore.Save(configuration);
        }

        var services = new ServiceCollection();

        // Infrastructure : stockage, journalisation, configuration
        services.AddSingleton(storagePaths);
        services.AddSingleton<IConfigurationStore>(configurationStore);
        // Journal durci : rédaction des secrets + chaîne d'intégrité (selon settings.json).
        var logger = new FileLogger(
            storagePaths.LogDirectory,
            configuration.LogMaxFileSizeMb,
            configuration.LogFilesToKeep,
            sanitize: configuration.Security.LogSanitizationEnabled,
            integrity: configuration.Security.LogIntegrityProtection);
        services.AddSingleton<ILogger>(logger);
        services.AddSingleton(new SqliteDatabase(storagePaths.DatabaseFile));

        // Dépôts et services de persistance
        services.AddSingleton<AuditRepository>();
        services.AddSingleton<IAuditService, AuditService>();
        services.AddSingleton<InventoryRepository>();
        services.AddSingleton<InventoryQueryRepository>();
        services.AddSingleton<AssistantTargetRepository>();
        services.AddSingleton<ComputerHealthRepository>();
        services.AddSingleton<DepartmentMappingService>();
        services.AddSingleton<IDepartmentMappingService>(sp => sp.GetRequiredService<DepartmentMappingService>());
        services.AddSingleton<DomainProfileRepository>();
        services.AddSingleton<GpoDeploymentRepository>();

        // PowerShell (exécuteurs, parsing, traduction d'erreurs)
        var executorFactory = new PowerShellExecutorFactory(logger);
        // Canal WinRM du mode distant : HTTPS strict par choix explicite,
        // négociation HTTPS→HTTP par défaut (repli journalisé, jamais silencieux).
        executorFactory.Transport = configuration.Security.ResolveWinRmTransport();
        logger.Information(
            "Démarrage | transport WinRM=" + executorFactory.Transport
            + " | mode=" + configuration.ExecutorMode
            + " | domaine=" + (string.IsNullOrWhiteSpace(configuration.DomainFqdn) ? "(non configuré)" : configuration.DomainFqdn));
        services.AddSingleton<IPowerShellExecutorFactory>(executorFactory);
        services.AddSingleton<PowerShellOutputParser>();
        services.AddSingleton<IErrorInterpreter, ErrorInterpreter>();

        // Services métier : santé, GPO, inventaire AD DS, disponibilité, RSoP
        // HealthCheckService implémente à la fois IHealthCheckService et
        // IAuthenticationService : une seule instance partagée.
        services.AddSingleton<HealthCheckService>();
        services.AddSingleton<IHealthCheckService>(sp => sp.GetRequiredService<HealthCheckService>());
        services.AddSingleton<IAuthenticationService>(sp => sp.GetRequiredService<HealthCheckService>());
        services.AddSingleton<IGpoService, GpoService>();
        services.AddSingleton<IGpoResultService, GpoResultService>();
        services.AddSingleton<IActiveDirectoryInventoryService, ActiveDirectoryInventoryService>();
        services.AddSingleton<IComputerAvailabilityService, ComputerAvailabilityService>();
        services.AddSingleton<IInventorySyncService, InventorySyncService>();
        services.AddSingleton<IComputerControlService, ComputerControlService>();

        // Annuaire, découverte réseau et déploiement de GPO
        services.AddSingleton<IDirectoryService, DirectoryService>();
        services.AddSingleton<NetworkDiscoveryService>();
        services.AddSingleton<INetworkDiscoveryService, NetworkDiscoveryService>();
        services.AddSingleton<IGpoDeploymentService, GpoDeploymentService>();

        // Déploiement réel des modèles du catalogue : GPO du domaine, ciblage
        // par groupe AD DS, liaison aux OU, puis gpupdate/gpresult sur les postes.
        services.AddSingleton<IGpoTemplateDeploymentService, GpoTemplateDeploymentService>();

        // Session implicite : détection automatique du domaine et du contrôleur
        // à partir de la session Windows courante (remplace l'écran de connexion).
        services.AddSingleton<ISessionBootstrapper, SessionBootstrapper>();

        // Diagnostic intelligent & assistant de réparation
        services.AddSingleton<IDiagnosticEngine, DiagnosticEngine>();
        services.AddSingleton<IRemediationService, RemediationService>();

        // Catalogue de modèles de stratégies
        services.AddSingleton<IGpoCatalogService, TemplateCatalog>();

        // Catalogue de scripts PowerShell réels (gpo_catalog.json embarqué)
        services.AddSingleton<IGpoScriptCatalog, GpoScriptCatalog>();

        // État partagé et navigation
        services.AddSingleton<ConnectionState>();
        // Garde de session : verrouillage automatique après inactivité (0 = désactivé).
        services.AddSingleton(new SessionGuard(
            configuration.Security.SessionTimeoutMinutes <= 0
                ? TimeSpan.MaxValue
                : TimeSpan.FromMinutes(configuration.Security.SessionTimeoutMinutes)));
        services.AddSingleton<ILauncherService, LauncherService>();
        services.AddSingleton<INavigationService, NavigationService>();

        // Outils d'administration Windows de la machine (console ADUC, gestion
        // de l'ordinateur, stratégies de groupe) et dossier « Outils
        // d'administration » du menu Démarrer.
        services.AddSingleton<AdminToolsService>();

        // ViewModels et vues (singletons : l'état de page survit à la navigation)
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<WorkspaceViewModel>();
        services.AddSingleton<GpoAssistantViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<DiagnosticsViewModel>();
        services.AddSingleton<ParcViewModel>();
        services.AddSingleton<ClientAccountsViewModel>();
        services.AddSingleton<DirectoryViewModel>();
        services.AddSingleton<ComputerDetailViewModel>();
        services.AddSingleton<DepartmentMappingViewModel>();
        services.AddSingleton<GpoListViewModel>();
        services.AddSingleton<CatalogViewModel>();
        services.AddSingleton<DeploymentsViewModel>();
        services.AddSingleton<AutomateGpoViewModel>();
        services.AddSingleton<AuditViewModel>();
        services.AddSingleton<SettingsViewModel>();

        // Fenêtre principale (singleton). Aucun écran de connexion : la session
        // Windows courante est l'identité employée par l'application.
        services.AddSingleton<MainWindow>();
        services.AddTransient<SessionLockWindow>();

        return services.BuildServiceProvider();
    }
}
