namespace ADOps.GpoManager.Core.Models;

/// <summary>Mode d'authentification de la connexion au domaine.</summary>
public enum CredentialMode
{
    /// <summary>Identité Windows courante (Kerberos / NTLM selon le canal).</summary>
    CurrentUser = 0,

    /// <summary>Compte de domaine explicite (mot de passe gardé en mémoire uniquement).</summary>
    Explicit = 1,
}
