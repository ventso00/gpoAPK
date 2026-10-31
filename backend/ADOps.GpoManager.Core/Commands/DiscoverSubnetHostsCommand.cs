using System.Globalization;
using System.Text.RegularExpressions;

namespace ADOps.GpoManager.Core.Commands;

/// <summary>
/// Découverte des hôtes d'un sous-réseau : balayage ICMP parallèle
/// (pool de runspaces, 64 threads) puis résolution DNS inverse.
/// S'exécute sur le contrôleur de domaine (mode distant) pour scanner
/// le masque réseau du serveur. Le préfixe réseau (3 premiers octets,
/// ex. « 192.168.1 ») est validé avant injection : aucune saisie libre
/// n'atteint le script.
/// </summary>
public sealed class DiscoverSubnetHostsCommand : PowerShellCommand
{
    private static readonly Regex PrefixRegex = new(
        @"^(\d{1,3}\.){2}\d{1,3}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string NetworkPrefix { get; }

    public int HostCount { get; }

    public override string Name => "DiscoverSubnetHosts";
    public override TimeSpan Timeout { get; } = TimeSpan.FromMinutes(4);

    public DiscoverSubnetHostsCommand(string networkPrefix, int hostCount = 254)
    {
        if (string.IsNullOrWhiteSpace(networkPrefix) || !PrefixRegex.IsMatch(networkPrefix.Trim()))
        {
            throw new ArgumentException(
                "Le préfixe réseau doit être formé de 3 octets IPv4 (ex. : 192.168.1).",
                nameof(networkPrefix));
        }

        foreach (var octet in networkPrefix.Trim().Split('.'))
        {
            if (!byte.TryParse(octet, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                throw new ArgumentException(
                    "Le préfixe réseau contient un octet invalide (0-255) : « " + networkPrefix + " ».",
                    nameof(networkPrefix));
            }
        }

        if (hostCount is < 1 or > 254)
        {
            throw new ArgumentOutOfRangeException(nameof(hostCount), "Le nombre d'hôtes doit être compris entre 1 et 254.");
        }

        NetworkPrefix = networkPrefix.Trim();
        HostCount = hostCount;
    }

    public override string ScriptBody
    {
        get
        {
            var prefix = NetworkPrefix.Replace("'", "''");
            var max = HostCount.ToString(CultureInfo.InvariantCulture);

            return
                "$networkPrefix = '" + prefix + "'\n" +
                "$maxHosts = " + max + "\n" +
                "$runspacePool = [runspacefactory]::CreateRunspacePool(1, 64)\n" +
                "$runspacePool.Open()\n" +
                "$pingScript = {\n" +
                "    param($ip)\n" +
                "    try {\n" +
                "        $ping = New-Object System.Net.NetworkInformation.Ping\n" +
                "        $reply = $ping.Send($ip, 1000)\n" +
                "        $reply.Status -eq 'Success'\n" +
                "    } catch { $false }\n" +
                "}\n" +
                "$pingJobs = @()\n" +
                "foreach ($i in 1..$maxHosts) {\n" +
                "    $ipAddress = $networkPrefix + '.' + $i\n" +
                "    $shell = [powershell]::Create()\n" +
                "    $shell.RunspacePool = $runspacePool\n" +
                "    [void]$shell.AddScript($pingScript).AddArgument($ipAddress)\n" +
                "    $pingJobs += [pscustomobject]@{ IpAddress = $ipAddress; Shell = $shell; Async = $shell.BeginInvoke() }\n" +
                "}\n" +
                "$onlineAddresses = New-Object System.Collections.Generic.List[string]\n" +
                "foreach ($job in $pingJobs) {\n" +
                "    try {\n" +
                "        $pingResult = $job.Shell.EndInvoke($job.Async)\n" +
                "        if ($pingResult -eq $true) { $onlineAddresses.Add($job.IpAddress) }\n" +
                "    } catch { }\n" +
                "    $job.Shell.Dispose()\n" +
                "}\n" +
                "$runspacePool.Dispose()\n" +
                "$discovered = @()\n" +
                "foreach ($ipAddress in $onlineAddresses) {\n" +
                "    $hostName = $null\n" +
                "    try { $hostName = [System.Net.Dns]::GetHostEntry($ipAddress).HostName } catch { }\n" +
                "    $discovered += [pscustomobject]@{ IpAddress = $ipAddress; HostName = $hostName; Online = $true }\n" +
                "}\n" +
                "$discovered";
        }
    }
}
