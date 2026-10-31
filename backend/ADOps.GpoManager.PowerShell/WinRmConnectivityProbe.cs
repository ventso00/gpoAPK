using System.Globalization;
using System.Net;
using System.Net.Sockets;
using ADOps.GpoManager.Core.Security;
using ADOps.GpoManager.PowerShell.Host;

namespace ADOps.GpoManager.PowerShell;

/// <summary>
/// Sonde de connectivité préalable à toute session WinRM : résolution DNS du nom
/// (enregistrement A/AAAA) puis test TCP des ports WSMan (5985 HTTP, 5986 HTTPS).
/// Sans elle, <c>Invoke-Command</c> échoue en produisant une enveloppe CLIXML
/// illisible et un code de sortie 1 sans explication exploitable.
/// </summary>
public sealed class WinRmConnectivityProbe
{
    private static readonly TimeSpan DnsTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TcpTimeout = TimeSpan.FromSeconds(2);

    private const string UnreachableMarker = "WSMan port 5985/5986 unreachable on";

    /// <summary>Résultat d'une sonde : adresse résolue et ports joignables.</summary>
    public sealed record ProbeResult(
        bool Resolved,
        string? IpAddress,
        bool HttpReachable,
        bool HttpsReachable)
    {
        public bool AnyPortReachable => HttpReachable || HttpsReachable;
    }

    private sealed record CacheEntry(DateTimeOffset ExpiresUtc, ProbeResult Result);

    // Une sonde par cible, pas par commande : les consoles du poste enchaînent
    // gpupdate/gpresult et chaque tentative WinRM coûterait sinon deux probes.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, CacheEntry> Cache = new();

    public async Task<ProbeResult> ProbeAsync(string targetFqdn, CancellationToken cancellationToken)
    {
        var key = targetFqdn.ToUpperInvariant();
        if (Cache.TryGetValue(key, out var cached) && cached.ExpiresUtc > DateTimeOffset.UtcNow)
        {
            return cached.Result;
        }

        var result = await ProbeCoreAsync(targetFqdn, cancellationToken).ConfigureAwait(false);
        Cache[key] = new CacheEntry(
            DateTimeOffset.UtcNow.AddSeconds(result.Resolved && result.AnyPortReachable ? 60 : 10),
            result);
        return result;
    }

    private static async Task<ProbeResult> ProbeCoreAsync(string targetFqdn, CancellationToken cancellationToken)
    {
        string? ipAddress = null;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(DnsTimeout);
            var addresses = await Dns.GetHostAddressesAsync(targetFqdn, timeoutCts.Token).ConfigureAwait(false);
            ipAddress = addresses
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.ToString())
                .FirstOrDefault()
                ?? addresses.FirstOrDefault()?.ToString();
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            // Non résolu : le diagnostic complet sera construit par l'appelant.
        }

        var http = ipAddress is not null
            && await TryConnectAsync(ipAddress, WinRmPorts.Http, cancellationToken).ConfigureAwait(false);
        var https = ipAddress is not null
            && await TryConnectAsync(ipAddress, WinRmPorts.Https, cancellationToken).ConfigureAwait(false);

        return new ProbeResult(Resolved: ipAddress is not null, ipAddress, http, https);
    }

    private static async Task<bool> TryConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TcpTimeout);
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, timeoutCts.Token).ConfigureAwait(false);
            return client.Connected;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ArgumentException)
        {
            return false;
        }
        finally
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    /// <summary>
    /// Résultat d'échec synthétique lorsque la sonde prouve que la cible est
    /// injoignable : le message décrit l'état réel (DNS, ports) au lieu de
    /// relayer l'erreur sérialisée du transport WinRM.
    /// </summary>
    public static PowerShellExecutionResult BuildUnreachableResult(
        string targetFqdn,
        ProbeResult probe,
        WinRmTransportMode transport,
        TimeSpan duration)
    {
        var title = probe.Resolved
            ? UnreachableMarker + " " + targetFqdn + " (" + probe.IpAddress + ")"
            : "DNS name does not exist: " + targetFqdn;

        var detail = probe switch
        {
            { Resolved: false } =>
                "Le serveur DNS du domaine ne connaît aucun enregistrement A/AAAA pour « " + targetFqdn + " ». " +
                "Le poste est peut-être éteint, hors domaine, ou son enregistrement DNS a expiré.",
            { HttpReachable: false, HttpsReachable: false } =>
                "Le poste répond au DNS (" + probe.IpAddress + ") mais les ports WSMan 5985 (HTTP) et 5986 (HTTPS) sont fermés : " +
                "le service Gestion à distance Windows (WinRM) n'est pas activé sur la cible ou le pare-feu du poste le bloque.",
            { HttpReachable: false } =>
                "Le poste répond au DNS (" + probe.IpAddress + ") mais le port WSMan HTTP 5985 est fermé " +
                "(WinRM non configuré en HTTP sur la cible).",
            _ =>
                "Le poste répond au DNS (" + probe.IpAddress + ") mais le port WSMan HTTPS 5986 est fermé : aucun écouteur HTTPS n'est configuré sur la cible.",
        };

        var remediation = transport == WinRmTransportMode.Https
            ? "Configurer l'écouteur HTTPS sur le poste (certificat + WinRM listener sur 5986) ou choisir « Négocier » dans Paramètres ▸ Canal WinRM."
            : "Sur le poste, en tant qu'administrateur du domaine : Enable-PSRemoting -Force, puis vérifier le pare-feu (port 5985). " +
              "Si le nom ne résout pas : ipconfig /registerdns sur le poste et vérifier que la machine est toujours jointe au domaine.";

        var stderr = title + "\n" + detail + "\n" + remediation;
        return new PowerShellExecutionResult(1, string.Empty, stderr, TimedOut: false, duration);
    }

    /// <summary>Message court pour le journal.</summary>
    public static string Describe(ProbeResult probe)
        => string.Format(
            CultureInfo.InvariantCulture,
            "dns={0} ip={1} tcp5985={2} tcp5986={3}",
            probe.Resolved ? "ok" : "fail",
            probe.IpAddress ?? "-",
            probe.HttpReachable ? "open" : "closed",
            probe.HttpsReachable ? "open" : "closed");
}
