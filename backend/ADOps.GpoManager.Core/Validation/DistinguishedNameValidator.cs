using System.Text.RegularExpressions;

namespace ADOps.GpoManager.Core.Validation;

/// <summary>
/// Validation des DistinguishedName LDAP (cibles des liens de GPO).
/// Contrainte de sécurité : aucune valeur n'est injectée dans un script
/// PowerShell sans avoir été validée puis échappée (guillemets doublés).
/// </summary>
public static class DistinguishedNameValidator
{
    private const int MaxDnLength = 1024;

    // Composants RDN autorisés : CN=, OU=, DC=, C= — valeurs sans virgule ni caractères de contrôle.
    private static readonly Regex DnRegex = new(
        @"^(CN|OU|DC|C)=[^,]+(,(CN|OU|DC|C)=[^,]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Valide un DistinguishedName LDAP (ex. : OU=Production,DC=sarytany,DC=local).</summary>
    public static (bool IsValid, string? Error) ValidateDistinguishedName(string? value, string label = "le DistinguishedName")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (false, label + " est requis.");
        }

        var candidate = value.Trim();
        if (candidate.Length > MaxDnLength)
        {
            return (false, label + " dépasse " + MaxDnLength + " caractères : « " + candidate[..Math.Min(80, candidate.Length)] + "… ».");
        }

        if (candidate.Any(char.IsControl))
        {
            return (false, label + " contient des caractères de contrôle interdits.");
        }

        if (!DnRegex.IsMatch(candidate))
        {
            return (false, label + " n'est pas un DistinguishedName LDAP valide. Format attendu : OU=MonOU,DC=domaine,DC=local (composants CN=, OU=, DC=, C=).");
        }

        return (true, null);
    }

    /// <summary>
    /// Échappe une valeur pour un littéral PowerShell entre guillemets simples :
    /// les guillemets simples sont doublés (''), ce qui rend l'injection impossible.
    /// </summary>
    public static string EscapePowerShellLiteral(string value) => value.Replace("'", "''");
}
