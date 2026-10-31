using ADOps.GpoManager.Core.Models;

namespace ADOps.GpoManager.Core.Services;

/// <summary>Service de connexion : valide les prérequis et établit le contexte de domaine.</summary>
public interface IAuthenticationService
{
    /// <param name="progress">
    /// Callback optionnel émis pour chaque étape dès qu'elle se termine,
    /// afin d'animer le health check en direct pendant l'exécution.
    /// </param>
    Task<HealthCheckReport> RunDiagnosticsAsync(
        string serverFqdn,
        string domainFqdn,
        ConnectionCredentials? credentials = null,
        IProgress<HealthCheckStep>? progress = null,
        CancellationToken cancellationToken = default);
}
