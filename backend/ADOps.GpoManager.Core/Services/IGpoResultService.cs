using ADOps.GpoManager.Core.Models;

namespace ADOps.GpoManager.Core.Services;

/// <summary>Résultats effectifs (RSoP / rapport GPO) lus sur les postes, jamais devinés.</summary>
public interface IGpoResultService
{
    Task<OperationResult<ComputerGpoResult>> GetResultantSetAsync(string computerName, DomainContext context, CancellationToken cancellationToken = default);
}
