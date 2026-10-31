using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Network;
using ADOps.GpoManager.Core.Services;

namespace ADOps.GpoManager.Infrastructure.Services;

/// <summary>
/// Détection de l'identité de la session Windows. Elle s'appuie sur le jeton
/// du processus : aucun identifiant n'est saisi, aucun mot de passe n'est
/// demandé ni conservé. La détection ne lève jamais — un échec produit une
/// identité partielle, exploitable par l'appelant.
/// </summary>
public static class WindowsSessionProbe
{
    /// <summary>Lit l'identité courante : compte, domaine de session et élévation.</summary>
    public static SessionIdentity Detect()
    {
        var userName = Environment.UserName;
        string? userDomain = null;
        var elevated = false;

        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            if (!string.IsNullOrWhiteSpace(identity.Name))
            {
                userName = identity.Name;
            }

            // « DOMAINE\utilisateur » : le domaine du jeton est l'information utile.
            var separator = userName.IndexOf('\\');
            if (separator > 0)
            {
                userDomain = userName[..separator];
            }

            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            elevated = principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            // Jeton inaccessible : l'identité reste partielle, l'application
            // fonctionnera en lecture seule sur le cache local.
        }

        var domain = MachineDomain.Detect();

        return new SessionIdentity
        {
            UserName = userName,
            UserDomainName = userDomain,
            DomainName = domain.DomainName,
            DcAddress = domain.DcAddress,
            IsElevated = elevated,
            IsDomainJoined = domain.IsDomainJoined,
            DomainSource = domain.Source,
        };
    }
}
