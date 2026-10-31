using ADOps.GpoManager.Core.Validation;

namespace ADOps.GpoManager.Core.Commands;

/// <summary>
/// Diagnostic DNS intelligent :
/// - réponse ICMP du contrôleur (Test-Connection) ;
/// - résolution du nom du contrôleur (A) ;
/// - résolution du domaine (A) ;
/// - recherche des enregistrements SRV AD (_ldap._tcp.dc._msdcs.&lt;domaine&gt;) ;
/// - configuration DNS du client (serveurs configurés).
/// Succès = résolution du domaine ET présence d'au moins un enregistrement SRV.
/// </summary>
public sealed class DiagnoseDnsCommand : PowerShellCommand
{
    public string ServerFqdn { get; }
    public string DomainFqdn { get; }

    public override string Name => "DiagnoseDns";
    public override TimeSpan Timeout { get; } = TimeSpan.FromSeconds(90);

    public DiagnoseDnsCommand(string serverFqdn, string domainFqdn)
    {
        var (serverOk, serverError) = DomainValidator.ValidateServerFqdn(serverFqdn);
        if (!serverOk)
        {
            throw new ArgumentException(serverError, nameof(serverFqdn));
        }

        var (domainOk, domainError) = DomainValidator.ValidateDomainName(domainFqdn);
        if (!domainOk)
        {
            throw new ArgumentException(domainError, nameof(domainFqdn));
        }

        ServerFqdn = DomainValidator.NormalizeFqdn(serverFqdn);
        DomainFqdn = DomainValidator.NormalizeFqdn(domainFqdn);
    }

    public override string ScriptBody
    {
        get
        {
            var server = DistinguishedNameValidator.EscapePowerShellLiteral(ServerFqdn);
            var domain = DistinguishedNameValidator.EscapePowerShellLiteral(DomainFqdn);

            // Sortie JSON stricte : un objet unique, consommé par le moteur de diagnostic.
            return
                "$server = '" + server + "'\n" +
                "$domain = '" + domain + "'\n" +
                "$isIpLiteral = $server -match '^\\d{1,3}(\\.\\d{1,3}){3}$'\n" +
                "$pingOk = $false\n" +
                "try { $pingOk = (Test-Connection -ComputerName $server -Count 2 -Quiet -ErrorAction SilentlyContinue) } catch {}\n" +
                "$serverIps = @()\n" +
                "try { $serverIps = @((Resolve-DnsName -Name $server -Type A -ErrorAction SilentlyContinue | Where-Object { $_.IPAddress } | Select-Object -ExpandProperty IPAddress)) } catch {}\n" +
                "$domainIps = @()\n" +
                "try { $domainIps = @((Resolve-DnsName -Name $domain -Type A -ErrorAction SilentlyContinue | Where-Object { $_.IPAddress } | Select-Object -ExpandProperty IPAddress)) } catch {}\n" +
                "$srvRecords = @()\n" +
                "try { $srvRecords = @(Resolve-DnsName -Name ('_ldap._tcp.dc._msdcs.' + $domain) -Type SRV -ErrorAction SilentlyContinue) } catch {}\n" +
                "$srvTargets = @($srvRecords | Where-Object { $_.NameTarget } | Select-Object -ExpandProperty NameTarget | Sort-Object -Unique)\n" +
                "$clientDns = @()\n" +
                "try { $clientDns = @(Get-DnsClientServerAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object { $_.ServerAddresses } | Select-Object -ExpandProperty ServerAddresses | Sort-Object -Unique) } catch {}\n" +
                "$domainResolved = ($domainIps.Count -gt 0)\n" +
                "$srvOk = ($srvTargets.Count -gt 0)\n" +
                "@{\n" +
                "    TargetReachable = $pingOk\n" +
                "    TargetIp       = $(if ($serverIps.Count -gt 0) { $serverIps[0].ToString() } elseif ($isIpLiteral) { $server } else { $null })\n" +
                "    DomainResolved = $domainResolved\n" +
                "    DomainIps      = @($domainIps | ForEach-Object { $_.ToString() })\n" +
                "    SrvRecordCount = $srvTargets.Count\n" +
                "    SrvTargets     = @($srvTargets)\n" +
                "    ClientDnsServers = @($clientDns | ForEach-Object { $_.ToString() })\n" +
                "    Success        = ($domainResolved -and $srvOk)\n" +
                "}";
        }
    }
}
