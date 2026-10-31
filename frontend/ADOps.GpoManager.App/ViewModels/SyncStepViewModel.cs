using ADOps.GpoManager.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ADOps.GpoManager.App.ViewModels;

/// <summary>
/// Adaptateur d'affichage d'une étape de synchronisation. L'étape est
/// pré-annoncée (« en cours ») puis mise à jour en place quand elle se
/// termine : l'interface montre l'avancement réel de la collecte.
/// </summary>
public sealed partial class SyncStepViewModel : ObservableObject
{
    [ObservableProperty]
    private string _stateText = "En attente…";

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private string _durationText = string.Empty;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isFailed;

    public string Name { get; }

    public SyncStepViewModel(string name)
    {
        Name = name;
    }

    /// <summary>Applique le résultat d'une étape (terminée ou en cours).</summary>
    public void Apply(SyncStepResult result)
    {
        StateText = result.StateText;
        Message = result.Message;
        DurationText = result.Duration.TotalMilliseconds < 1000
            ? result.Duration.TotalMilliseconds.ToString("0") + " ms"
            : result.Duration.TotalSeconds.ToString("0.0") + " s";
        IsRunning = result.State == SyncStepState.Running;
        IsFailed = result.State == SyncStepState.Failed;
    }
}
