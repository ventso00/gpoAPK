using ADOps.GpoManager.Core.Models;

namespace ADOps.GpoManager.Core.Services;

public interface IHealthCheckService
{
    Task<HealthCheckReport> RunAsync(string serverFqdn, string domainFqdn, CancellationToken cancellationToken = default);
}
