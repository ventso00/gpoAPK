namespace ADOps.GpoManager.Core.Security;

/// <summary>
/// Ports WinRM standard (RFC : 5985 / 5986). Déclarés dans Core pour être
/// accessibles à la fois aux commandes typées (Core) et aux exécuteurs
/// (ADOps.GpoManager.PowerShell), sans dépendance inverse.
/// </summary>
public static class WinRmPorts
{
    /// <summary>HTTP en clair — canal par défaut de <c>Enable-PSRemoting</c>.</summary>
    public const int Http = 5985;

    /// <summary>HTTPS — nécessite un écouteur créé par l'administrateur et un certificat de confiance.</summary>
    public const int Https = 5986;
}