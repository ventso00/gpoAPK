using ADOps.GpoManager.Core.Models;

namespace ADOps.GpoManager.Core.Services;

/// <summary>
/// Découverte du sous-réseau du serveur cible : balayage ICMP du masque
/// réseau (/24) et résolution DNS inverse, croisé avec l'inventaire AD DS.
/// Un ping échoué ne prouve pas qu'une machine est éteinte (ICMP est
/// souvent bloqué) : les machines connues d'AD DS mais non joignables
/// sont listées comme « hors ligne », jamais supprimées.
/// </summary>
public interface INetworkDiscoveryService
{
    Task<OperationResult<IReadOnlyList<NetworkHostInfo>>> DiscoverAsync(
        DomainContext context,
        IReadOnlyList<ComputerInfo> knownComputers,
        CancellationToken cancellationToken = default);
}
