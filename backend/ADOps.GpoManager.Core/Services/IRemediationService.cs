using ADOps.GpoManager.Core.Diagnostics;
using ADOps.GpoManager.Core.Models;

namespace ADOps.GpoManager.Core.Services;

/// <summary>
/// Assistant de réparation automatisée :
/// - <see cref="BuildPlan"/> produit un plan prévisualisable (aucune modification) ;
/// - <see cref="ExecuteAsync"/> applique les actions confirmées (jamais sans confirmation explicite) ;
/// - <see cref="RetestAsync"/> rejoue les contrôles impactés pour vérifier le succès.
/// Le mode diagnostic (DryRun) ne modifie strictement rien.
/// </summary>
public interface IRemediationService
{
    /// <summary>Construit le plan de remédiation à partir d'un rapport de diagnostic.</summary>
    RemediationPlan BuildPlan(DiagnosticReport report);

    /// <summary>
    /// Applique les actions sélectionnées du plan.
    /// <paramref name="confirmed"/> doit être true : aucune action sensible n'est exécutée sans
    /// confirmation explicite de l'utilisateur. <paramref name="dryRun"/> simule sans rien modifier.
    /// </summary>
    Task<IReadOnlyList<RemediationExecutionResult>> ExecuteAsync(
        DomainContext context,
        RemediationPlan plan,
        bool confirmed,
        bool dryRun,
        CancellationToken cancellationToken = default);

    /// <summary>Rejoue les contrôles cibles des actions (validation post-correction).</summary>
    Task<DiagnosticReport> RetestAsync(
        DomainContext context,
        IReadOnlyList<string> checkIds,
        CancellationToken cancellationToken = default);
}
