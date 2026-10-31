namespace ADOps.GpoManager.Core.Models;

/// <summary>État d'une étape de synchronisation, émis en direct pendant la collecte.</summary>
public enum SyncStepState
{
    NotRun = 0,
    Running = 1,
    Succeeded = 2,
    Partial = 3,
    Failed = 4,
}

/// <summary>
/// Résultat d'une étape de la synchronisation (lecture d'un jeu d'objets AD DS).
/// Émis au fil de l'eau pour que l'interface affiche l'avancement au lieu de
/// laisser croire à un blocage.
/// </summary>
public sealed class SyncStepResult
{
    /// <summary>Nom lisible de l'étape (ex. : « Ordinateurs »).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>État de l'étape.</summary>
    public SyncStepState State { get; init; } = SyncStepState.NotRun;

    /// <summary>Nombre d'objets lus.</summary>
    public int Count { get; init; }

    /// <summary>Message affiché (détail, avertissement ou cause d'échec).</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Durée de l'étape.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Libellé court de l'état (pour l'interface).</summary>
    public string StateText => State switch
    {
        SyncStepState.Running => "En cours…",
        SyncStepState.Succeeded => "OK",
        SyncStepState.Partial => "Partiel",
        SyncStepState.Failed => "Échec",
        _ => "—",
    };
}
