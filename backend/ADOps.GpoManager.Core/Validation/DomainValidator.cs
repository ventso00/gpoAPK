using System.Text.RegularExpressions;

namespace ADOps.GpoManager.Core.Validation;

/// <summary>Validation des noms DNS (FQDN de serveur et nom de domaine).</summary>
public static class DomainValidator
{
    private const int MaxFqdnLength = 253;
    private static readonly Regex Ipv4Regex = new(@"^(\d{1,3}\.){3}\d{1,3}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Valide un FQDN de serveur (hôte DNS ou adresse IPv4).</summary>
    public static (bool IsValid, string? Error) ValidateServerFqdn(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (false, "Le FQDN du serveur est requis.");
        }

        var candidate = value.Trim().TrimEnd('.');

        if (IsIPv4(candidate))
        {
            return (true, null);
        }

        return ValidateHostname(candidate, "le FQDN du serveur");
    }

    /// <summary>Valide un nom de domaine DNS (au moins deux étiquettes, ex. : ventso.test).</summary>
    public static (bool IsValid, string? Error) ValidateDomainName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (false, "Le nom de domaine est requis.");
        }

        var candidate = value.Trim().TrimEnd('.');
        var (isValid, error) = ValidateHostname(candidate, "le nom de domaine");
        if (!isValid)
        {
            return (false, error);
        }

        if (!candidate.Contains('.'))
        {
            return (false, "Le nom de domaine doit être un nom DNS complet (ex. : ventso.test). Les noms à une seule étiquette (style NetBIOS) ne sont pas acceptés ici.");
        }

        return (true, null);
    }

    /// <summary>Normalise un FQDN : trim, minuscules, sans point terminal.</summary>
    public static string NormalizeFqdn(string value) => value.Trim().TrimEnd('.').ToLowerInvariant();

    /// <summary>Vrai si la valeur est une adresse IPv4 littérale (ex. : 192.168.1.10).</summary>
    public static bool IsIPv4Address(string? value)
        => !string.IsNullOrWhiteSpace(value) && IsIPv4(value.Trim());

    private static (bool IsValid, string? Error) ValidateHostname(string candidate, string label)
    {
        // Une chaîne qui ressemble à une IPv4 mais contient des octets hors range
        // est rejetée explicitement : l'utilisateur visait probablement une adresse IP.
        if (Ipv4Regex.IsMatch(candidate) && !IsIPv4(candidate))
        {
            return (false, label + " ressemble à une adresse IPv4 mais contient un octet invalide (0-255) : « " + candidate + " ».");
        }

        if (candidate.Length == 0 || candidate.Length > MaxFqdnLength)
        {
            return (false, label + " est vide ou dépasse 253 caractères.");
        }

        foreach (var part in candidate.Split('.'))
        {
            if (part.Length == 0 || part.Length > 63)
            {
                return (false, label + " contient une étiquette vide ou trop longue (63 caractères maximum) : « " + part + " ».");
            }

            if (part.StartsWith('-') || part.EndsWith('-'))
            {
                return (false, label + " contient une étiquette commençant ou finissant par un tiret : « " + part + " ».");
            }

            foreach (var c in part)
            {
                var isAllowed = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-';
                if (!isAllowed)
                {
                    return (false, label + " contient un caractère non autorisé « " + c + " » dans « " + part + " ». Seuls les lettres, chiffres et tirets sont acceptés.");
                }
            }
        }

        return (true, null);
    }

    private static bool IsIPv4(string candidate)
    {
        if (!Ipv4Regex.IsMatch(candidate))
        {
            return false;
        }

        foreach (var octet in candidate.Split('.'))
        {
            if (!byte.TryParse(octet, out _))
            {
                return false;
            }
        }

        return true;
    }
}
