using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Services;

namespace ADOps.GpoManager.Infrastructure.Storage;

/// <summary>
/// Service d'audit : chaque opération est enregistrée en SQLite avec l'identité
/// Windows courante et le contexte enrichi (serveur, domaine, GUID, cibles,
/// états, durée, code d'erreur, identifiant d'opération). Aucun secret n'est audité.
/// </summary>
public sealed class AuditService : IAuditService
{
    private readonly AuditRepository _repository;
    private readonly ILogger _logger;

    public AuditService(AuditRepository repository, ILogger logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task LogAsync(
        string action,
        string target,
        AuditResult result,
        string message,
        string? details = null,
        AuditContext? context = null)
    {
        var entry = new AuditEntry
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            UserName = System.Security.Principal.WindowsIdentity.GetCurrent()?.Name ?? Environment.UserName,
            Action = action,
            Target = target,
            Result = result,
            Message = message,
            Details = details,
            OperationId = context?.OperationId,
            Server = context?.Server,
            Domain = context?.Domain,
            ObjectGuid = context?.ObjectGuid,
            Targets = context?.Targets,
            InitialState = context?.InitialState,
            RequestedState = context?.RequestedState,
            ErrorCode = context?.ErrorCode,
            Duration = context?.Duration,
        };

        _logger.Debug("Audit " + action + " | " + target + " | " + result + " | " + message);
        await _repository.InsertAsync(entry).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<AuditEntry>> GetRecentAsync(int maxEntries = 200) => _repository.GetRecentAsync(maxEntries);

    public Task<int> GetCountAsync() => _repository.GetCountAsync();
}
