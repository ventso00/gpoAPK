using ADOps.GpoManager.Core.Models;

namespace ADOps.GpoManager.Core.Services;

/// <summary>
/// Annuaire : lecture et modification des utilisateurs du domaine (AD DS reste
/// la source de vérité). Chaque opération est une commande typée, jamais une
/// saisie libre.
/// </summary>
public interface IDirectoryService
{
    Task<OperationResult<IReadOnlyList<UserInfo>>> ListUsersAsync(
        DomainContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Déplace un utilisateur vers une autre unité d'organisation.</summary>
    Task<OperationResult<string>> MoveToOuAsync(
        DomainContext context,
        string userDistinguishedName,
        string targetOuDistinguishedName,
        CancellationToken cancellationToken = default);
}
