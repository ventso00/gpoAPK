namespace ADOps.GpoManager.Core.Models;

/// <summary>
/// Utilisateur lu dans AD DS via Get-ADUser.
/// AD DS reste la source de vérité : aucune donnée n'est inventée,
/// les attributs absents sont simplement nulls.
/// </summary>
public sealed class UserInfo
{
    public string SamAccountName { get; init; } = string.Empty;
    public string DistinguishedName { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string? GivenName { get; init; }
    public string? Surname { get; init; }
    public string? Email { get; init; }
    public string? Title { get; init; }
    public string? Department { get; init; }
    public bool Enabled { get; init; }
    public DateTimeOffset? LastLogonUtc { get; init; }

    /// <summary>
    /// Vrai si le compte est membre d'un groupe d'administration du domaine
    /// (Domain Admins, Enterprise Admins ou Administrateurs). Cette
    /// information pilote le filtrage de sécurité des GPO : un compte
    /// d'administration ne doit pas recevoir une stratégie destinée aux
    /// utilisateurs standards.
    /// </summary>
    public bool IsAdministrator { get; init; }

    /// <summary>Vrai si le compte a été verrouillé par un administrateur.</summary>
    public bool AccountLocked { get; init; }

    /// <summary>Vrai si le mot de passe n'expire jamais.</summary>
    public bool PasswordNeverExpires { get; init; }

    /// <summary>OU parente déduite du DistinguishedName (le premier composant RDN est retiré).</summary>
    public string? OuDistinguishedName { get; init; }

    /// <summary>Libellé affiché : nom d'affichage ou login.</summary>
    public string DisplayText => string.IsNullOrWhiteSpace(DisplayName) ? SamAccountName : DisplayName;

    public string StatusText => Enabled ? "Activé" : "Désactivé";

    /// <summary>Libellé du statut global du compte (état + verrouillage + privilège).</summary>
    public string AccountStatusText
    {
        get
        {
            if (!Enabled)
            {
                return "Désactivé";
            }

            if (AccountLocked)
            {
                return "Verrouillé";
            }

            return IsAdministrator ? "Administrateur" : "Standard";
        }
    }
}
