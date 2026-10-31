using ADOps.GpoManager.Core.Models;

namespace ADOps.GpoManager.Core.Services;

/// <summary>Lecture de l'inventaire AD DS : OU, ordinateurs, comptes utilisateurs, GPO et liens. AD DS reste la source de vérité.</summary>
public interface IActiveDirectoryInventoryService
{
    /// <param name="progress">
    /// Callback optionnel émis à chaque étape (démarrée puis terminée) afin
    /// d'afficher la collecte en direct dans l'interface.
    /// </param>
    Task<SyncResult> ReadInventoryAsync(
        DomainContext context,
        IProgress<SyncStepResult>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Orchestration de la synchronisation : lecture AD DS + persistance locale + audit.</summary>
public interface IInventorySyncService
{
    /// <param name="progress">
    /// Callback optionnel émis pour chaque étape de la collecte.
    /// </param>
    Task<SyncRunSummary> SyncAsync(
        DomainContext context,
        IProgress<SyncStepResult>? progress = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SyncRunSummary>> GetHistoryAsync(int maxRuns = 20);
}
