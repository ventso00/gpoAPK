using ADOps.GpoManager.Core.Validation;

namespace ADOps.GpoManager.Core.Commands;

/// <summary>
/// Diagnostic WinRM :
/// - test des ports 5985 (HTTP) et 5986 (HTTPS) par connexion TCP (n'utilise pas WinRM :
///   un service WinRM hors ligne reste ainsi diagnostiquable — principe « fail-safe ») ;
/// - réponse de l'écouteur WinRM cible (Test-WSMan) ;
/// - état du service WinRM et règles de pare-feu locales (périmètre de cette machine).
/// </summary>
public sealed class DiagnoseWinRmCommand : PowerShellCommand
{
    public string ServerFqdn { get; }

    public override string Name => "DiagnoseWinRm";
    public override TimeSpan Timeout { get; } = TimeSpan.FromSeconds(90);

    public DiagnoseWinRmCommand(string serverFqdn)
    {
        var (isValid, error) = DomainValidator.ValidateServerFqdn(serverFqdn);
        if (!isValid)
        {
            throw new ArgumentException(error, nameof(serverFqdn));
        }

        ServerFqdn = DomainValidator.NormalizeFqdn(serverFqdn);
    }

    public override string ScriptBody
    {
        get
        {
            var server = DistinguishedNameValidator.EscapePowerShellLiteral(ServerFqdn);

            return
                "$server = '" + server + "'\n" +
                "function Test-TcpPort($target, $port) {\n" +
                "    try {\n" +
                "        $client = New-Object System.Net.Sockets.TcpClient\n" +
                "        $async = $client.BeginConnect($target, $port, $null, $null)\n" +
                "        $connected = $async.AsyncWaitHandle.WaitOne(3000, $false)\n" +
                "        if ($connected) { try { $client.EndConnect($async) } catch { return $false } }\n" +
                "        $client.Close()\n" +
                "        return $connected\n" +
                "    } catch { return $false }\n" +
                "}\n" +
                "$port5985 = Test-TcpPort $server 5985\n" +
                "$port5986 = Test-TcpPort $server 5986\n" +
                "$wsManResponds = $false\n" +
                "try { $wsManResponds = [bool](Test-WSMan -ComputerName $server -ErrorAction SilentlyContinue) } catch {}\n" +
                "$serviceState = 'Inconnu'\n" +
                "try { $serviceState = (Get-Service -Name WinRM -ErrorAction SilentlyContinue).Status.ToString() } catch {}\n" +
                "$firewallRules = 0\n" +
                "try { $firewallRules = @(Get-NetFirewallRule -Group 'Windows Remote Management' -ErrorAction SilentlyContinue).Count } catch {}\n" +
                "@{\n" +
                "    HttpPort5985Open  = $port5985\n" +
                "    HttpsPort5986Open = $port5986\n" +
                "    WsManResponds     = $wsManResponds\n" +
                "    LocalServiceState = $serviceState\n" +
                "    LocalFirewallRuleCount = $firewallRules\n" +
                "    Success           = ($port5985 -or $port5986) -and $wsManResponds\n" +
                "}";
        }
    }
}
