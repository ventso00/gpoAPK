using System.IO;
using System.Windows;
using ADOps.GpoManager.App.Services;
using ADOps.GpoManager.App.ViewModels;
using ADOps.GpoManager.App.Views;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Security;
using ADOps.GpoManager.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ADOps.GpoManager.App;

public partial class App : Application
{
    private IServiceProvider? _serviceProvider;

    /// <summary>Conteneur de services (exposé pour les vues : garde de session, navigation).</summary>
    public IServiceProvider ServiceProvider =>
        _serviceProvider ?? throw new InvalidOperationException("Le conteneur de services n'est pas encore initialisé.");

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Filet de sécurité : une exception sur un thread secondaire (tâche de
        // fond, continuation de Task) échappe au dispatcher et tuerait le
        // processus sans laisser de trace. Elle est journalisée pour que la
        // cause reste lisible dans le journal de l'application.
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);

        // Mode publication : génération du manifeste d'intégrité puis sortie.
        //   ADOps.GpoManager.App.exe --generate-integrity-manifest
        if (e.Args.Any(arg => string.Equals(arg, "--generate-integrity-manifest", StringComparison.OrdinalIgnoreCase)))
        {
            var directory = AssemblyIntegrityGuard.CurrentDirectory();
            AssemblyIntegrityGuard.CreateManifest(directory);
            MessageBox.Show(
                "Manifeste d'intégrité généré :\n" + Path.Combine(directory, AssemblyIntegrityGuard.ManifestFileName),
                "ADOps GPO Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _serviceProvider = Bootstrapper.ConfigureServices();

        VerifyBinaryIntegrity();

        // Aucun écran de connexion : l'application travaille avec la session
        // Windows courante (jeton du processus, mode local RSAT). La fenêtre
        // principale s'ouvre immédiatement ; la détection de session puis la
        // synchronisation se déroulent dessus, avec un retour en direct dans la
        // barre supérieure.
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();

        // L'affichage du tableau de bord ne doit jamais dépendre de la
        // réussite de la synchronisation : en cas d'échec, la fenêtre reste
        // ouverte et affiche le diagnostic.
        _ = StartSessionAsync();
    }

    /// <summary>
    /// Séquence de démarrage : détection automatique de la session de domaine,
    /// puis synchronisation immédiate de l'inventaire (OU, ordinateurs, comptes
    /// clients, GPO et liens).
    /// </summary>
    private async Task StartSessionAsync()
    {
        try
        {
            var audit = _serviceProvider?.GetService<IAuditService>();
            if (audit is not null)
            {
                await audit.LogAsync(
                    AuditActions.ApplicationStarted,
                    Environment.MachineName,
                    AuditResult.Information,
                    "Application démarrée — session implicite (compte Windows courant).");
            }

            var main = _serviceProvider?.GetRequiredService<MainViewModel>();
            if (main is not null)
            {
                await main.InitializeAsync();
            }
        }
        catch (Exception ex)
        {
            _serviceProvider?.GetService<ILogger>()?.Error("Séquence de démarrage échouée", ex);
        }
    }

    /// <summary>
    /// Contrôle anti-altération des binaires (manifeste SHA-256 généré à la publication).
    /// Une anomalie est journalisée en Critical et signalée à l'administrateur.
    /// </summary>
    private void VerifyBinaryIntegrity()
    {
        try
        {
            var tampered = AssemblyIntegrityGuard.VerifyDirectory(AssemblyIntegrityGuard.CurrentDirectory());
            if (tampered.Count == 0)
            {
                return;
            }

            _serviceProvider?.GetService<ILogger>()?.Critical(
                "Intégrité des binaires compromise : " + string.Join(", ", tampered));

            MessageBox.Show(
                "L'intégrité des binaires de l'application n'est pas vérifiée :\n- "
                + string.Join("\n- ", tampered)
                + "\n\nRéinstallez l'application depuis une source de confiance.",
                "ADOps GPO Manager — Alerte d'intégrité",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            // Le contrôle d'intégrité ne doit jamais empêcher le démarrage.
            _serviceProvider?.GetService<ILogger>()?.Warning("Vérification d'intégrité impossible : " + ex.Message);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        (_serviceProvider?.GetService<ILogger>() as IDisposable)?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        _serviceProvider?.GetService<ILogger>()?.Critical("Exception non gérée sur le thread UI.", e.Exception);
        MessageBox.Show(
            "Une erreur inattendue s'est produite :\n" + e.Exception.Message
            + "\n\nL'application reste ouverte : le tableau de bord et le journal sont consultables."
            + "\n\nJournal : %LOCALAPPDATA%\\ADOps\\GpoManager\\logs\\adops.log",
            "ADOps GPO Manager — Erreur",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        // L'exception est neutralisée : l'application ne doit pas se fermer
        // sous les pieds de l'administrateur au milieu d'une synchronisation.
        e.Handled = true;
    }

    /// <summary>
    /// Exception non rattrapée sur un thread secondaire : le processus va
    /// s'arrêter. La cause est journalisée pour être exploitable.
    /// </summary>
    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;
        _serviceProvider?.GetService<ILogger>()?.Critical(
            "Exception non gérée hors du thread UI : " + (exception?.ToString() ?? "(inconnue)"),
            exception);
    }

    /// <summary>
    /// Exception d'une tâche sans observateur : elle n'arrête pas le
    /// processus, mais elle est journalisée (cause souvent utile).
    /// </summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _serviceProvider?.GetService<ILogger>()?.Warning(
            "Exception de tâche non observée : " + e.Exception);
        e.SetObserved();
    }
}
