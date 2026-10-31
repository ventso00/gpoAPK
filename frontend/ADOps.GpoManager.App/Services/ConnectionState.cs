using ADOps.GpoManager.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ADOps.GpoManager.App.Services;

/// <summary>
/// État de connexion partagé entre les pages (singleton injecté dans les ViewModels).
/// Les credentials ne vivent qu'ici, en mémoire, le temps de la session.
/// </summary>
public sealed class ConnectionState : ObservableObject
{
    private DomainContext? _current;
    private int? _lastGpoCount;
    private DateTimeOffset? _lastSyncUtc;
    private SyncState _lastSyncState = SyncState.NotRun;
    private string? _lastSyncSummary;
    private string? _healthSummary;
    private TimeSpan? _lastSyncDuration;
    private IReadOnlyList<string> _lastSyncWarnings = Array.Empty<string>();
    private string? _serverAddress;

    public DomainContext? Current
    {
        get => _current;
        private set => SetProperty(ref _current, value);
    }

    public bool IsConnected => _current is not null;

    public ConnectionCredentials? Credentials => _current?.Credentials;

    public int? LastGpoCount
    {
        get => _lastGpoCount;
        set => SetProperty(ref _lastGpoCount, value);
    }

    public DateTimeOffset? LastSyncUtc
    {
        get => _lastSyncUtc;
        set => SetProperty(ref _lastSyncUtc, value);
    }

    public SyncState LastSyncState
    {
        get => _lastSyncState;
        set => SetProperty(ref _lastSyncState, value);
    }

    public string? LastSyncSummary
    {
        get => _lastSyncSummary;
        set => SetProperty(ref _lastSyncSummary, value);
    }

    /// <summary>Résumé du contrôle de santé de la session (affiché au tableau de bord).</summary>
    public string? HealthSummary
    {
        get => _healthSummary;
        set => SetProperty(ref _healthSummary, value);
    }

    /// <summary>Durée de la dernière synchronisation.</summary>
    public TimeSpan? LastSyncDuration
    {
        get => _lastSyncDuration;
        set => SetProperty(ref _lastSyncDuration, value);
    }

    /// <summary>Avertissements de la dernière synchronisation.</summary>
    public IReadOnlyList<string> LastSyncWarnings
    {
        get => _lastSyncWarnings;
        set => SetProperty(ref _lastSyncWarnings, value);
    }

    /// <summary>Adresse IP du contrôleur cible (si elle est connue).</summary>
    public string? ServerAddress
    {
        get => _serverAddress;
        set => SetProperty(ref _serverAddress, value);
    }

    /// <summary>
    /// Vrai pendant qu'une synchronisation est en cours. Empêche deux
    /// collectes concurrentes (la synchronisation de démarrage et celle
    /// déclenchée par une page) de tourner en parallèle.
    /// </summary>
    public bool IsSyncInProgress { get; set; }

    public void SetConnection(DomainContext context)
    {
        SetProperty(ref _current, context, nameof(Current));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(Credentials));
    }

    public void SetSyncResult(SyncRunSummary summary)
    {
        LastSyncUtc = summary.StartedUtc;
        LastSyncState = summary.State;
        LastSyncWarnings = summary.Warnings;
        LastSyncDuration = summary.FinishedUtc - summary.StartedUtc;
        LastSyncSummary = summary.State switch
        {
            SyncState.Succeeded => "Réussie",
            SyncState.Partial => "Partielle",
            SyncState.Failed => "Échouée",
            SyncState.Running => "En cours",
            _ => "Non exécutée",
        };
    }

    /// <summary>
    /// Marque la synchronisation comme échouée (exception non structurée).
    /// Évite que l'interface retente une synchronisation à chaque navigation.
    /// </summary>
    public void MarkSyncFailed(string error)
    {
        LastSyncUtc = DateTimeOffset.UtcNow;
        LastSyncState = SyncState.Failed;
        LastSyncSummary = "Échouée : " + error;
    }

    public void Clear()
    {
        _current?.Credentials.Dispose();
        SetProperty(ref _current, null, nameof(Current));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(Credentials));
    }
}
