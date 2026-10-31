using ADOps.GpoManager.Core.Models;

namespace ADOps.GpoManager.Core.Services;

/// <summary>
/// Détection automatique de la session de domaine : elle remplace l'écran de
/// connexion. L'identité employée est celle de la session Windows courante
/// (jeton du processus) — aucun mot de passe n'est demandé, saisi ni stocké.
/// </summary>
public interface ISessionBootstrapper
{
    /// <param name="progress">
    /// Callback optionnel émis pour chaque étape du contrôle de santé, afin
    /// d'afficher la détection en direct pendant son exécution.
    /// </param>
    Task<SessionBootstrapResult> BootstrapAsync(
        IProgress<HealthCheckStep>? progress = null,
        CancellationToken cancellationToken = default);
}
