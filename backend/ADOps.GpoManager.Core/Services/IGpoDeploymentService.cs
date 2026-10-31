using ADOps.GpoManager.Core.Commands;
using ADOps.GpoManager.Core.Models;

namespace ADOps.GpoManager.Core.Services;

/// <summary>
/// Moteur de déploiement des GPO :
/// - <see cref="PlanDeploymentAsync"/> : validation de la sélection et calcul
///   du rapport d'impact (utilisateurs, ordinateurs, OU ciblés) ;
/// - <see cref="DeployAsync"/> : application des liens (New-GPLink) ;
/// - <see cref="UnlinkGpoAsync"/> : suppression d'un lien (Remove-GPLink).
/// Chaque opération modifiative est auditée. Les erreurs sont structurées
/// (titre, cause, résolution), jamais un simple code de sortie.
/// </summary>
public interface IGpoDeploymentService
{
    /// <summary>
    /// Valide la sélection (GPO × OU cibles) et calcule le rapport d'impact.
    /// Les liens existants (cache local ou lecture préalable) permettent de
    /// signaler les doublons. Aucune modification n'est effectuée ici.
    /// </summary>
    Task<OperationResult<GpoDeploymentPlan>> PlanDeploymentAsync(
        DomainContext context,
        IReadOnlyList<GpoSummary> selectedGpos,
        IReadOnlyList<OuInfo> targetOus,
        IReadOnlyList<UserInfo> users,
        IReadOnlyList<ComputerInfo> computers,
        IReadOnlyList<GpoLinkInfo> existingLinks,
        CancellationToken cancellationToken = default);

    /// <summary>Applique le plan validé : crée chaque lien de GPO (New-GPLink).</summary>
    Task<OperationResult<GpoDeploymentResult>> DeployAsync(
        DomainContext context,
        GpoDeploymentPlan plan,
        CancellationToken cancellationToken = default);

    /// <summary>Supprime un lien de GPO existant (Remove-GPLink).</summary>
    Task<OperationResult<string>> UnlinkGpoAsync(
        DomainContext context,
        Guid gpoId,
        string targetDistinguishedName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Plan de déploiement exprimé en « machines et comptes clients ».
    /// Les cibles sont regroupées par unité d'organisation : une seule
    /// entrée (GPO × OU) est créée par conteneur, quel que soit le nombre de
    /// postes qu'il contient. Aucune modification n'est effectuée ici.
    /// </summary>
    Task<OperationResult<MachineDeploymentPlan>> PlanMachineDeploymentAsync(
        DomainContext context,
        IReadOnlyList<GpoSummary> selectedGpos,
        IReadOnlyList<ComputerInfo> targetComputers,
        IReadOnlyList<UserInfo> targetAccounts,
        IReadOnlyList<GpoLinkInfo> existingLinks,
        string? securityGroupDn = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applique un plan « machines » : création des liens (New-GPLink),
    /// filtre de sécurité éventuel, puis rafraîchissement (gpupdate /force)
    /// et vérification (gpresult) des postes cibles. Chaque étape émet une
    /// ligne de journal pour le terminal d'exécution.
    /// </summary>
    Task<OperationResult<MachineDeploymentResult>> DeployToMachinesAsync(
        DomainContext context,
        MachineDeploymentPlan plan,
        bool applySecurityFilter,
        bool refreshTargets,
        IProgress<ExecutionLine>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Crée une GPO dans le domaine à partir d'un modèle (option
    /// « Ajouter une GPO ») et renvoie son identifiant.
    /// </summary>
    Task<OperationResult<GpoSummary>> CreateGpoAsync(
        DomainContext context,
        string gpoName,
        IReadOnlyList<GpoRegistrySetting> settings,
        string? comment = null,
        CancellationToken cancellationToken = default);
}
