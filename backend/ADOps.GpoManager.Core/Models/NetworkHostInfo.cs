namespace ADOps.GpoManager.Core.Models;

/// <summary>Origine d'un hôte découvert sur le masque réseau.</summary>
public enum NetworkHostOrigin
{
    /// <summary>Découvert par le balayage réseau uniquement (inconnu d'AD DS).</summary>
    Discovered = 0,

    /// <summary>Connu d'AD DS mais non joignable lors du balayage.</summary>
    AdComputer = 1,

    /// <summary>En ligne sur le réseau ET connu d'AD DS.</summary>
    Both = 2,
}

/// <summary>
/// Hôte du masque réseau du serveur cible : issu du balayage ICMP
/// (en ligne) et/ou de l'inventaire AD DS (connu du domaine).
/// L'état est mesuré, jamais supposé : un ping échoué ne prouve pas
/// qu'une machine est éteinte (ICMP est souvent bloqué).
/// </summary>
public sealed class NetworkHostInfo
{
    public string IpAddress { get; init; } = string.Empty;
    public string? HostName { get; init; }
    public bool Online { get; init; }
    public NetworkHostOrigin Origin { get; init; }
    public string? AdDistinguishedName { get; init; }
    public string? AdOuDistinguishedName { get; init; }
    public string? OperatingSystem { get; init; }
    public DateTimeOffset CheckedAtUtc { get; init; }

    /// <summary>Vrai si l'hôte est un objet ordinateur d'AD DS.</summary>
    public bool IsAdComputer => Origin is NetworkHostOrigin.AdComputer or NetworkHostOrigin.Both;

    public string StatusText => Online ? "En ligne" : "Hors ligne (connu d'AD DS)";

    public string SourceText => Origin switch
    {
        NetworkHostOrigin.Discovered => "Découverte réseau",
        NetworkHostOrigin.AdComputer => "AD DS (non joignable)",
        NetworkHostOrigin.Both => "Réseau + AD DS",
        _ => "Inconnu",
    };

    public string IpDisplay => string.IsNullOrWhiteSpace(IpAddress) ? "—" : IpAddress;
}
