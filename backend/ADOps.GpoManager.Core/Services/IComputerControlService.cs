using ADOps.GpoManager.Core.Models;

namespace ADOps.GpoManager.Core.Services;

/// <summary>
/// Contrôle AD DS et système des postes clients :
/// - activation / désactivation du compte d'ordinateur ;
/// - déplacement vers une autre unité d'organisation ;
/// - redémarrage et extinction à distance (WinRM).
/// Toute action est confirmée dans l'interface, auditée, et n'est jamais
/// simulée : le résultat renvoyé décrit ce qui a réellement été fait.
/// </summary>
public interface IComputerControlService
{
    /// <summary>Active ou désactive le compte d'ordinateur dans AD DS.</summary>
    Task<OperationResult<string>> SetEnabledAsync(
        DomainContext context,
        ComputerInfo computer,
        bool enabled,
        CancellationToken cancellationToken = default);

    /// <summary>Déplace le compte d'ordinateur vers une autre OU.</summary>
    Task<OperationResult<string>> MoveToOuAsync(
        DomainContext context,
        ComputerInfo computer,
        string targetOuDistinguishedName,
        CancellationToken cancellationToken = default);

    /// <summary>Redémarre le poste client (délai de 15 secondes, serveurs refusés).</summary>
    Task<OperationResult<string>> RestartAsync(
        DomainContext context,
        ComputerInfo computer,
        CancellationToken cancellationToken = default);

    /// <summary>Éteint le poste client (délai de 15 secondes, serveurs refusés).</summary>
    Task<OperationResult<string>> ShutdownAsync(
        DomainContext context,
        ComputerInfo computer,
        CancellationToken cancellationToken = default);
}
