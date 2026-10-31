using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Services;

namespace ADOps.GpoManager.Infrastructure.Storage;

/// <summary>
/// Orchestration de la synchronisation : lecture AD DS (via le service d'inventaire),
/// persistance locale, historique et audit. AD DS reste la source de vérité ;
/// la base locale est un cache horodaté.
/// </summary>
public sealed class InventorySyncService : IInventorySyncService
{
    private readonly IActiveDirectoryInventoryService _inventory;
    private readonly InventoryRepository _repository;
    private readonly IAuditService _audit;
    private readonly ILogger _logger;

    public InventorySyncService(
        IActiveDirectoryInventoryService inventory,
        InventoryRepository repository,
        IAuditService audit,
        ILogger logger)
    {
        _inventory = inventory;
        _repository = repository;
        _audit = audit;
        _logger = logger;
    }

    public async Task<SyncRunSummary> SyncAsync(
        DomainContext context,
        IProgress<SyncStepResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.Information("InventorySync | début | cible=" + context.ServerFqdn);
        try
        {
            var result = await _inventory.ReadInventoryAsync(context, progress, cancellationToken);
            var summary = await _repository.PersistSyncAsync(result);

            await _audit.LogAsync(
                AuditActions.InventorySync,
                context.ServerFqdn,
                summary.State == SyncState.Failed
                    ? AuditResult.Failure
                    : summary.State == SyncState.Partial ? AuditResult.Warning : AuditResult.Success,
                "Synchronisation " + summary.State.ToString().ToLowerInvariant()
                    + " : " + summary.ComputersRead + " ordinateurs, " + summary.OrganizationalUnitsRead
                    + " OU, " + summary.UsersRead + " comptes, " + summary.GposRead + " GPO, "
                    + summary.LinksRead + " liens.",
                string.Join(" | ", result.Warnings),
                new AuditContext(
                    Server: context.ServerFqdn,
                    Domain: context.DomainFqdn,
                    Duration: result.Duration,
                    OperationId: Guid.NewGuid().ToString("N")));

            _logger.Information("InventorySync | terminé | état=" + summary.State);
            return summary;
        }
        catch (OperationCanceledException)
        {
            await _audit.LogAsync(
                AuditActions.InventorySync,
                context.ServerFqdn,
                AuditResult.Warning,
                "Synchronisation annulée par l'utilisateur.",
                context: new AuditContext(Server: context.ServerFqdn, Domain: context.DomainFqdn));
            return new SyncRunSummary
            {
                StartedUtc = DateTimeOffset.UtcNow,
                FinishedUtc = DateTimeOffset.UtcNow,
                State = SyncState.Failed,
                Error = "Synchronisation annulée.",
            };
        }
        catch (Exception ex)
        {
            _logger.Error("InventorySync | échec", ex);
            await _audit.LogAsync(
                AuditActions.InventorySync,
                context.ServerFqdn,
                AuditResult.Failure,
                "Échec de la synchronisation : " + ex.Message,
                ex.ToString(),
                new AuditContext(
                    Server: context.ServerFqdn,
                    Domain: context.DomainFqdn,
                    ErrorCode: ex.GetType().Name,
                    OperationId: Guid.NewGuid().ToString("N")));
            return new SyncRunSummary
            {
                StartedUtc = DateTimeOffset.UtcNow,
                FinishedUtc = DateTimeOffset.UtcNow,
                State = SyncState.Failed,
                Error = ex.Message,
            };
        }
    }

    public Task<IReadOnlyList<SyncRunSummary>> GetHistoryAsync(int maxRuns = 20)
        => _repository.GetHistoryAsync(maxRuns);
}
