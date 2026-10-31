using ADOps.GpoManager.Core.Models;

namespace ADOps.GpoManager.Core.Services;

/// <summary>
/// Déploiement réel des modèles du catalogue : matérialisation des valeurs de
/// registre dans une GPO du domaine, ciblage par groupe de sécurité AD DS,
/// liaison aux OU, sans exiger WinRM sur les postes clients.
/// </summary>
public interface IGpoTemplateDeploymentService
{
    /// <summary>
    /// Déploie les modèles sélectionnés en créant les GPOs dans le domaine,
    /// en créant les groupes de sécurité, en ajoutant les utilisateurs cibles
    /// aux groupes, et en liant les GPOs aux OU appropriées.
    /// </summary>
    Task<OperationResult<TemplateDeploymentResult>> DeployAsync(
        DomainContext context,
        IReadOnlyList<TemplateDeploymentSelection> selections,
        IReadOnlyList<UserInfo> users,
        bool applySecurityFiltering,
        IProgress<ExecutionLine>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Retire un utilisateur du ciblage d'une GPO.</summary>
    Task<OperationResult<string>> DetachFromUserAsync(
        DomainContext context,
        GpoDeploymentRecord record,
        IProgress<ExecutionLine>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lit les GPO appliquées d'un poste (gpresult /r) - optionnel, requiert WinRM.</summary>
    Task<OperationResult<IReadOnlyList<string>>> VerifyComputerAsync(
        DomainContext context,
        string computerName,
        CancellationToken cancellationToken = default);

    /// <summary>Force l'application des stratégies sur un poste (gpupdate /force) - optionnel, requiert WinRM.</summary>
    Task<OperationResult<string>> RefreshComputerAsync(
        DomainContext context,
        string computerName,
        CancellationToken cancellationToken = default);
}
