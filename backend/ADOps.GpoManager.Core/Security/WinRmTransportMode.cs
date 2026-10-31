namespace ADOps.GpoManager.Core.Security;

/// <summary>
/// Canal WinRM utilisé par l'exécuteur distant.
/// </summary>
/// <remarks>
/// Historique : ce choix était porté par un unique booléen
/// <c>Security.RequireWinRmHttps</c>. Sur une plateforme de test VMware
/// (Windows Server 2019 promu en contrôleur de domaine) le service WinRM
/// n'écoute par défaut que sur HTTP 5985 : le mode <see cref="Https"/>
/// échoue alors sur l'absence d'écouteur 5986, et surtout sur le certificat
/// auto-signé généré par <c>Enable-PSRemoting</c>, que l'application refuse
/// de contourner. L'énumération rend le repli explicite et lisible.
/// </remarks>
public enum WinRmTransportMode
{
    /// <summary>
    /// HTTPS 5986 avec validation complète du certificat (autorité, nom, révocation).
    /// Aucun repli : c'est le mode le plus strict, à réserver à une cible
    /// disposant d'un certificat émis par une autorité de confiance.
    /// </summary>
    Https = 0,

    /// <summary>
    /// HTTP 5985 en clair. Aucune confidentialité ni intégrité du canal WSMan.
    /// À n'utiliser que sur un réseau de test isolé, ou lorsque HTTPS est impossible.
    /// </summary>
    Http = 1,

    /// <summary>
    /// Négociation : HTTPS 5986 en premier ; si et seulement si l'échec est
    /// imputable au canal chiffré (listener 5986 absent, certificat non de
    /// confiance, connexion refusée sur ce port), la commande est rejouée une
    /// fois en HTTP 5985. Le repli est journalisé en <c>Warning</c>, audité et
    /// restitué dans le rapport de santé — il n'est jamais silencieux.
    /// </summary>
    Auto = 2,
}