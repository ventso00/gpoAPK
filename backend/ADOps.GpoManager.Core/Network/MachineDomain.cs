namespace ADOps.GpoManager.Core.Network;

/// <summary>
/// Détection du domaine Active Directory auquel la machine courante est jointe.
/// Trois sources, jamais inventées :
/// 1. la variable d'environnement <c>USERDNSDOMAIN</c>, positionnée par
///    Windows sur un poste joint — c'est la source faisant autorité pour le
///    nom de domaine ;
/// 2. l'API Netlogon (<c>DsGetDcName</c> sans nom de domaine) — elle donne le
///    contrôleur principal (nom DNS et adresse IP) ;
/// 3. le nom de domaine déduit du FQDN du contrôleur (derniers deux
///    étiquettes), en dernier recours.
/// </summary>
public static class MachineDomain
{
    /// <summary>Résultat de la détection.</summary>
    public sealed record Info(
        string? DomainName,
        string? DcFqdn,
        string? DcAddress,
        bool IsDomainJoined,
        string Source)
    {
        /// <summary>Domaine inconnu (poste hors domaine ou détection impossible).</summary>
        public static Info Unknown(string source) => new(null, null, null, false, source);
    }

    /// <summary>Détecte le domaine et le contrôleur principal de la machine courante.</summary>
    public static Info Detect()
    {
        DcLocator.DiscoveredController? controller = null;
        try
        {
            controller = DcLocator.DiscoverPrimaryController();
        }
        catch
        {
            // La détection n'est jamais bloquante : l'appelant décide.
        }

        var fromEnvironment = Normalize(Environment.GetEnvironmentVariable("USERDNSDOMAIN"));
        var derived = DeriveDomainFromFqdn(controller?.Fqdn);
        var domain = fromEnvironment ?? derived;

        if (domain is null)
        {
            return Info.Unknown(
                controller is null
                    ? "Ce poste ne semble pas joint à un domaine Active Directory."
                    : "Domaine non détecté (ni USERDNSDOMAIN, ni FQDN de contrôleur exploitable).");
        }

        var source = fromEnvironment is not null
            ? "USERDNSDOMAIN + Netlogon (DsGetDcName)"
            : "Netlogon (DsGetDcName) — domaine déduit du FQDN du contrôleur";

        return new Info(domain, controller?.Fqdn, controller?.Address, true, source);
    }

    /// <summary>Normalise un nom de domaine : minuscules, sans point terminal.</summary>
    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var candidate = value.Trim().TrimEnd('.').ToLowerInvariant();
        // Un nom de domaine doit avoir au moins deux étiquettes.
        return candidate.Contains('.', StringComparison.Ordinal) ? candidate : null;
    }

    /// <summary>Déduit le domaine des deux dernières étiquettes du FQDN du contrôleur.</summary>
    private static string? DeriveDomainFromFqdn(string? fqdn)
    {
        if (string.IsNullOrWhiteSpace(fqdn))
        {
            return null;
        }

        var labels = fqdn.Trim().TrimEnd('.').Split('.');
        if (labels.Length < 3)
        {
            return null;
        }

        return (labels[^2] + "." + labels[^1]).ToLowerInvariant();
    }
}
