using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace ADOps.GpoManager.Core.Network;

/// <summary>
/// Localisation du contrôleur de domaine via l'API Netlogon (DsGetDcName).
/// Utilité principale : convertir une adresse IP saisie par l'administrateur
/// en FQDN exploitable. Kerberos refuse catégoriquement une cible IP (aucun
/// SPN n'est associé à une adresse) : WinRM échouerait avec « L'authentification
/// Kerberos ne peut pas être utilisée lorsque la destination est une adresse IP ».
/// L'API Netlogon fonctionne même sans zone DNS inverse (PTR).
/// </summary>
public static class DcLocator
{
    /// <summary>Contrôleur découvert : nom DNS complet + adresse (IP ou nom).</summary>
    public sealed record DiscoveredDc(string Fqdn, string Address);

    /// <summary>
    /// Contrôleur principal de la machine courante : nom DNS et adresse IP.
    /// </summary>
    public sealed record DiscoveredController(string Fqdn, string Address);

    /// <summary>
    /// Découvre le contrôleur principal de la machine courante.
    /// L'appel à l'API Netlogon est fait **sans** nom de domaine ni nom de
    /// serveur : Windows renvoie alors le DC du domaine auquel ce poste est
    /// joint, avec son nom DNS et son adresse.
    /// <para>
    /// Structure lue (<c>DC_INFO</c>) : un pointeur sur le nom du contrôleur
    /// (offset 0, forme UNC « \\serveur.domaine.local ») puis un pointeur sur
    /// son adresse IP (offset suivant, forme UNC « \\192.168.0.1 »). Le nom de
    /// domaine n'y figure pas : il est obtenu par
    /// <see cref="MachineDomain.Detect"/>.
    /// </para>
    /// </summary>
    public static DiscoveredController? DiscoverPrimaryController()
    {
        IntPtr info;
        try
        {
            var hr = DsGetDcName(null, null, IntPtr.Zero, IntPtr.Zero, 0, out info);
            if (hr != 0 || info == IntPtr.Zero)
            {
                return null;
            }
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }

        try
        {
            var dcName = Marshal.PtrToStringUni(Marshal.ReadIntPtr(info, 0));
            var address = Marshal.PtrToStringUni(Marshal.ReadIntPtr(info, IntPtr.Size));
            if (string.IsNullOrWhiteSpace(dcName))
            {
                return null;
            }

            return new DiscoveredController(NormalizeDcName(dcName), NormalizeAddress(address));
        }
        finally
        {
            NetApiBufferFree(info);
        }
    }

    /// <summary>
    /// Découvre le contrôleur de domaine d'un domaine. Le domaine doit être un
    /// nom DNS complet (ex. : sarytany.local) — c'est la seule forme acceptée
    /// par l'application (ValidateDomainName). Les saisies NetBIOS retourneraient
    /// un nom de contrôleur court. Retourne null si le domaine est injoignable
    /// (poste non joint, DNS en panne…).
    /// </summary>
    public static DiscoveredDc? Discover(string? domainFqdn)
    {
        if (string.IsNullOrWhiteSpace(domainFqdn))
        {
            return null;
        }

        IntPtr info;
        try
        {
            var hr = DsGetDcName(null, domainFqdn.Trim(), IntPtr.Zero, IntPtr.Zero, 0, out info);
            if (hr != 0 || info == IntPtr.Zero)
            {
                return null;
            }
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }

        try
        {
            // Lecture manuelle des pointeurs : fiable sur toutes les versions
            // de .NET (le marshaling struct complet est plus fragile).
            var name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(info, 0));
            var address = Marshal.PtrToStringUni(Marshal.ReadIntPtr(info, IntPtr.Size));
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return new DiscoveredDc(NormalizeDcName(name), NormalizeAddress(address));
        }
        finally
        {
            NetApiBufferFree(info);
        }
    }

    /// <summary>
    /// Retourne le FQDN du contrôleur dont l'adresse correspond exactement à
    /// l'adresse saisie par l'utilisateur (comparaison IP directe puis
    /// résolution DNS du nom découvert). Retourne null si aucune correspondance.
    /// </summary>
    public static string? MatchByAddress(string? typedAddress, string? domainFqdn)
    {
        if (!IPAddress.TryParse(typedAddress?.Trim(), out var typed))
        {
            return null;
        }

        var dc = Discover(domainFqdn);
        if (dc is null)
        {
            return null;
        }

        // 1) L'adresse renvoyée par Netlogon est une IP comparable telle quelle.
        if (IPAddress.TryParse(dc.Address, out var dcAddress) && dcAddress.Equals(typed))
        {
            return dc.Fqdn;
        }

        // 2) Sinon, résolution A du nom découvert et comparaison des adresses.
        try
        {
            foreach (var resolved in Dns.GetHostAddresses(dc.Fqdn))
            {
                if (resolved.Equals(typed))
                {
                    return dc.Fqdn;
                }
            }
        }
        catch (SocketException)
        {
            // DNS indisponible : pas de correspondance démontrable.
        }
        catch (ArgumentException)
        {
            // Nom d'hôte invalide : pas de correspondance démontrable.
        }

        return null;
    }

    /// <summary>Retire les préfixes « \\ » éventuels d'un nom de contrôleur.</summary>
    public static string NormalizeDcName(string raw) => raw.Trim().TrimStart('\\');

    /// <summary>
    /// Retire les préfixes « \\ » d'une adresse renvoyée par Netlogon
    /// (« \\192.168.0.1 » → « 192.168.0.1 ») et les préfixes UNC « \\?\ ».
    /// </summary>
    public static string NormalizeAddress(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        return raw.Trim().TrimStart('\\').TrimStart('?').Trim();
    }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int DsGetDcName(
        string? computerName,
        string? domainName,
        IntPtr domainGuid,
        IntPtr siteName,
        int flags,
        out IntPtr domainControllerInfo);

    [DllImport("netapi32.dll")]
    private static extern void NetApiBufferFree(IntPtr buffer);
}
