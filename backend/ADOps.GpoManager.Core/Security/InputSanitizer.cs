using System.Text;
using System.Text.RegularExpressions;

namespace ADOps.GpoManager.Core.Security;

/// <summary>
/// Défense contre l'injection (commandes PowerShell, filtres LDAP).
/// Toute chaîne destine à être injectée dans un script ou un filtre LDAP
/// passe par ici : allow-list de caractères, échappement normalise,
/// refus des caractères de contrôle et des delimiters.
/// </summary>
public static partial class InputSanitizer
{
    /// <summary>Caractères autorisés dans un littéral PowerShell (single-quoted).</summary>
    private static readonly Regex AllowedLiteral = new(
        @"^[A-Za-z0-9 _.,:;@/=+()\[\]{}#&%$!?~^€*\-']{1,512}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Valide puis échappe un littéral PowerShell entre guillemets simples.
    /// Lève <see cref="ArgumentException"/> si la chaîne contient un caractère
    /// interdit (contrôle, nouvelle ligne, NUL, backtick).
    /// </summary>
    public static string ToPowerShellSingleQuotedLiteral(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("La valeur est requise.", parameterName);
        }

        if (value.Length > 512)
        {
            throw new ArgumentException("La valeur dépasse 512 caractères.", parameterName);
        }

        foreach (var c in value)
        {
            if (char.IsControl(c) || c == '`' || c == '"')
            {
                throw new ArgumentException(
                    "Caractère interdit (contrôle, guillemet ou apostrophe inversée) dans la valeur fournie.",
                    parameterName);
            }
        }

        if (!AllowedLiteral.IsMatch(value))
        {
            throw new ArgumentException(
                "La valeur contient des caractères non autorisés pour un littéral PowerShell.",
                parameterName);
        }

        // Échappement PowerShell : les guillemets simples sont doublés.
        return value.Replace("'", "''");
    }

    /// <summary>
    /// Échappe un filtre LDAP selon RFC 4515 (injection LDAP) :
    /// antislash, astérisque, parenthèses et NUL sont neutralisés.
    /// </summary>
    public static string EscapeLdapFilterValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': builder.Append("\\5c"); break;
                case '*': builder.Append("\\2a"); break;
                case '(': builder.Append("\\28"); break;
                case ')': builder.Append("\\29"); break;
                case '\0': builder.Append("\\00"); break;
                default: builder.Append(c); break;
            }
        }

        return builder.ToString();
    }

    /// <summary>Vrai si la chaîne est un fragment de script « sûr » (contrôle d'appoint).</summary>
    public static bool IsSafeScriptFragment(string? fragment)
    {
        if (string.IsNullOrEmpty(fragment))
        {
            return true;
        }

        foreach (var c in fragment)
        {
            if (char.IsControl(c) || c == '`')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Vérifie qu'un GUID est valide et non vide (protection des cibles de lien GPO).</summary>
    public static bool IsValidGuid(string? value)
        => Guid.TryParse(value, out var guid) && guid != Guid.Empty;
}
