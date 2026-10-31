namespace ADOps.GpoManager.Core.Security;

/// <summary>
/// Options de sécurité persistées dans settings.json — aucune donnée sensible
/// (uniquement des choix de configuration).
/// Valeurs par défaut volontairement strictes (secure by default).
/// </summary>
public sealed record SecurityOptions
{
    /// <summary>Canal WinRM de l'exécuteur distant.
    /// <see cref="WinRmTransportMode.Auto"/> (défaut) tente HTTPS 5986 puis se replie
    /// sur HTTP 5985 uniquement si l'échec vient du canal chiffré, en le signalant.
    /// Passer à <see cref="WinRmTransportMode.Https"/> pour interdire tout repli,
    /// ou <see cref="WinRmTransportMode.Http"/> pour imposer HTTP 5985.</summary>
    public WinRmTransportMode WinRmTransport { get; init; } = WinRmTransportMode.Auto;

    /// <summary>Ancienne option booléenne, conservée pour la compatibilité des
    /// fichiers settings.json existants. Elle n'est plus écrite par l'application :
    /// voir <see cref="ResolveWinRmTransport"/>. Si elle vaut <c>false</c>, elle
    /// l'emporte sur <see cref="WinRmTransport"/> (HTTP 5985 imposé).</summary>
    public bool RequireWinRmHttps { get; init; } = true;

    /// <summary>Verrouillage automatique de la console après inactivité (minutes). 0 = désactivé.</summary>
    public int SessionTimeoutMinutes { get; init; } = 10;

    /// <summary>Rédaction systématique des secrets et identifiants dans les journaux.</summary>
    public bool LogSanitizationEnabled { get; init; } = true;

    /// <summary>Chaîne de hachage des lignes de journal (preuve d'intégrité, détection d'altération).</summary>
    public bool LogIntegrityProtection { get; init; } = true;

    /// <summary>Vérification de l'intégrité des binaires au démarrage (anti-altération).</summary>
    public bool VerifyAssemblyIntegrity { get; init; } = true;

    /// <summary>Autorise la mémorisation du compte administrateur dans le coffre Windows.</summary>
    public bool AllowCredentialVault { get; init; } = true;

    /// <summary>
    /// Transport effectif : un <c>settings.json</c> existant qui désactive
    /// explicitement <see cref="RequireWinRmHttps"/> conserve le comportement
    /// historique (HTTP 5985 imposé) ; sinon <see cref="WinRmTransport"/> s'applique.
    /// </summary>
    public WinRmTransportMode ResolveWinRmTransport()
        => RequireWinRmHttps ? WinRmTransport : WinRmTransportMode.Http;

    /// <summary>
    /// Copie dont la valeur héritée <see cref="RequireWinRmHttps"/> est alignée sur
    /// le transport choisi — à utiliser avant enregistrement pour ne plus jamais
    /// écrire l'option dépréciée en contradiction avec <see cref="WinRmTransport"/>.
    /// </summary>
    public SecurityOptions WithWinRmTransport(WinRmTransportMode transport) => this with
    {
        WinRmTransport = transport,
        RequireWinRmHttps = transport != WinRmTransportMode.Http,
    };
}
