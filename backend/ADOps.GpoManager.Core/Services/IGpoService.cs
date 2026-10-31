using ADOps.GpoManager.Core.Models;

namespace ADOps.GpoManager.Core.Services;

public interface IGpoService
{
    Task<OperationResult<IReadOnlyList<GpoSummary>>> ListGposAsync(DomainContext context, CancellationToken cancellationToken = default);
}
