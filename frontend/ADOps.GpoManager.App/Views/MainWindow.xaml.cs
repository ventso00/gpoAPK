using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ADOps.GpoManager.Core.Security;
using Microsoft.Extensions.DependencyInjection;

namespace ADOps.GpoManager.App.Views;

public partial class MainWindow : Window
{
    private readonly SessionGuard _sessionGuard;
    private readonly ViewModels.MainViewModel _viewModel;
    private readonly DispatcherTimer _sessionTimer;
    private bool _sessionActive;

    public MainWindow(ViewModels.MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;

        // Taille adaptative : la fenêtre ne doit jamais dépasser la zone de
        // travail réelle (écrans 1366×768 fréquents en salle).
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(1240, Math.Max(940, workArea.Width - 32));
        Height = Math.Min(780, Math.Max(600, workArea.Height - 32));
        MinWidth = 900;
        MinHeight = 560;

        // La garde de session est résolue depuis le conteneur (durée configurée dans settings.json).
        var services = ((App)Application.Current).ServiceProvider;
        _sessionGuard = services.GetRequiredService<SessionGuard>();

        _sessionGuard.LockRequested += OnLockRequested;

        // Interaction utilisateur = activité (remet le compteur à zéro).
        PreviewMouseMove += (_, _) => _sessionGuard.ReportActivity();
        PreviewKeyDown += (_, _) => _sessionGuard.ReportActivity();
        PreviewMouseDown += (_, _) => _sessionGuard.ReportActivity();

        _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _sessionTimer.Tick += (_, _) =>
        {
            if (!_sessionActive)
            {
                return;
            }

            // Une synchronisation peut durer plusieurs minutes (lecture AD DS
            // sur un domaine volumineux) : l'administrateur n'y touche pas, et
            // le verrouillage automatique ne doit surtout pas masquer la
            // fenêtre au milieu de la collecte (symptôme « l'application
            // disparaît après la synchronisation »).
            if (_viewModel.IsSyncing)
            {
                _sessionGuard.ReportActivity();
                SessionLockStatus.Text = "Session : synchronisation en cours";
                return;
            }

            _sessionGuard.Tick();
            if (_sessionGuard.IdleTimeout == TimeSpan.MaxValue)
            {
                SessionLockStatus.Text = "Session : verrouillage auto désactivé";
                return;
            }

            var remaining = _sessionGuard.Remaining;
            SessionLockStatus.Text = remaining <= TimeSpan.Zero
                ? "Session : verrouillage…"
                : "Verrouillage auto dans " + ((int)remaining.TotalMinutes).ToString("00") + ":" + remaining.Seconds.ToString("00");
        };

        IsVisibleChanged += (_, args) =>
        {
            _sessionActive = (bool)args.NewValue;
            if (_sessionActive)
            {
                _sessionGuard.Unlock();
                _sessionGuard.ReportActivity();
            }
        };

        Loaded += (_, _) =>
        {
            _sessionActive = true;
            _sessionGuard.ReportActivity();
            _sessionTimer.Start();

            // La séquence de démarrage (App.StartSessionAsync) recharge déjà le
            // parc depuis le cache puis après la synchronisation AD DS.
        };

        Closed += (_, _) => _sessionTimer.Stop();
    }

    /// <summary>
    /// Verrouillage : la fenêtre est masquée et l'utilisateur doit déverrouiller
    /// explicitement. L'identité n'est pas redemandée (session Windows
    /// courante) : quitter la fenêtre de verrouillage ferme l'application.
    /// </summary>
    private void OnLockRequested(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _sessionActive = false;
            _sessionTimer.Stop();
            SessionLockStatus.Text = "Session : verrouillée";
            Hide();

            var services = ((App)Application.Current).ServiceProvider;
            var lockWindow = services.GetRequiredService<SessionLockWindow>();
            var unlocked = lockWindow.ShowDialog() == true && lockWindow.IsUnlocked;

            if (unlocked)
            {
                _sessionActive = true;
                _sessionGuard.Unlock();
                _sessionGuard.ReportActivity();
                _sessionTimer.Start();
                Show();
                Activate();
                return;
            }

            // Déverrouillage refusé : l'application se termine (état verrouillé).
            Application.Current.Shutdown();
        });
    }

    private void OnLockSessionClick(object sender, RoutedEventArgs e)
        => _sessionGuard.Lock();
}
