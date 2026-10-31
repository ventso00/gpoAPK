namespace ADOps.GpoManager.Core.Models;

/// <summary>
/// Identité de la session Windows courante : le compte qui exécute
/// l'application, son domaine de rattachement et son niveau d'élévation.
/// Cette identité remplace la saisie des identifiants sur l'écran de connexion :
/// elle est lue depuis le jeton de la session, jamais inventée.
/// <para>
/// Modèle volontairement agnostique : il ne contient aucune API Windows.
/// La détection est réalisée par <c>ADOps.GpoManager.Infrastructure</c>
/// (projet « net10.0-windows »), qui est le seul à dépendre de la plateforme.
/// </para>
/// </summary>
public sealed class SessionIdentity
{
    /// <summary>Compte Windows courant (DOMAINE\utilisateur).</summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>Domaine de la session (groupe de travail ou domaine AD).</summary>
    public string? UserDomainName { get; init; }

    /// <summary>Nom de domaine AD auquel la machine est jointe (null si groupe de travail).</summary>
    public string? DomainName { get; init; }

    /// <summary>Adresse IP du contrôleur principal, si elle est connue.</summary>
    public string? DcAddress { get; init; }

    /// <summary>Vrai si le processus tourne avec un jeton élevé (administrateur).</summary>
    public bool IsElevated { get; init; }

    /// <summary>Vrai si la machine est jointe à un domaine Active Directory.</summary>
    public bool IsDomainJoined { get; init; }

    /// <summary>Origine de la détection du domaine (traçabilité dans les journaux).</summary>
    public string DomainSource { get; init; } = string.Empty;

    /// <summary>
    /// Libellé du compte affiché dans l'interface. Le nom de session est déjà
    /// qualifié (« DOMAINE\utilisateur ») : le domaine n'est préfixé que
    /// lorsqu'il ne l'est pas encore, pour éviter « DOMAINE\DOMAINE\user ».
    /// </summary>
    public string DisplayText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(UserName))
            {
                return string.Empty;
            }

            if (UserName.Contains('\\', StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(UserDomainName))
            {
                return UserName;
            }

            return UserDomainName + "\\" + UserName;
        }
    }

    /// <summary>Libellé du niveau d'élévation.</summary>
    public string ElevationText => IsElevated ? "Élevé" : "Non élevé";
}
