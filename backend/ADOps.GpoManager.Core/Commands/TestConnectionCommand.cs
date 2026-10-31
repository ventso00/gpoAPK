using ADOps.GpoManager.Core.Security;
using ADOps.GpoManager.Core.Validation;

namespace ADOps.GpoManager.Core.Commands;

/// <summary>
/// Jeu de commandes typées du Health Check (cahier des charges §D) :
/// DNS, modules RSAT locaux, WinRM, authentification, accès AD DS, capacité GroupPolicy.
/// Le FQDN est validé à la construction : il ne peut jamais être injecté tel quel dans un script.
/// </summary>
public sealed class TestConnectionCommand
{
    public string ServerFqdn { get; }

    public PowerShellCommand DnsResolution { get; }
    public PowerShellCommand LocalModules { get; }
    public PowerShellCommand WinRmService { get; }
    public PowerShellCommand Authentication { get; }
    public PowerShellCommand AdAccess { get; }
    public PowerShellCommand GroupPolicyCapability { get; }

    /// <param name="transport">
    /// Canal WinRM sondé. <see cref="WinRmTransportMode.Auto"/> teste HTTPS 5986
    /// puis HTTP 5985 dans la même sonde, afin que le Health Check annonce le
    /// canal réellement disponible sur la cible (le mode d'exécution ensuite
    /// recommandé s'appuie dessus).
    /// </param>
    public TestConnectionCommand(string serverFqdn, WinRmTransportMode transport = WinRmTransportMode.Auto)
    {
        var (isValid, error) = DomainValidator.ValidateServerFqdn(serverFqdn);
        if (!isValid)
        {
            throw new ArgumentException(error, nameof(serverFqdn));
        }

        ServerFqdn = DomainValidator.NormalizeFqdn(serverFqdn);

        DnsResolution = new SimplePowerShellCommand(
            "DnsResolution",
            "Resolve-DnsName -Name '" + ServerFqdn + "' -Type A -ErrorAction Stop | Select-Object -First 1 Name, IPAddress, QueryType",
            TimeSpan.FromSeconds(20));

        LocalModules = new SimplePowerShellCommand(
            "LocalModules",
            "Get-Module -ListAvailable -Name GroupPolicy,ActiveDirectory -ErrorAction SilentlyContinue | Select-Object Name, Version",
            TimeSpan.FromSeconds(30));

        WinRmService = new SimplePowerShellCommand(
            "WinRmService",
            BuildWinRmProbe(transport),
            TimeSpan.FromSeconds(90));

        Authentication = new SimplePowerShellCommand(
            "Authentication",
            "Invoke-Command -ComputerName '" + ServerFqdn + "' -ErrorAction Stop -ScriptBlock { $env:USERNAME }",
            TimeSpan.FromSeconds(60));

        AdAccess = new TestAdAccessCommand();

        GroupPolicyCapability = new TestGroupPolicyCapabilityCommand();
    }

    /// <summary>
    /// Sonde Test-WSMan correspondant au canal configuré. La forme de sortie
    /// reste identique (ProductVendor / ProductVersion) quel que soit le canal,
    /// afin que l'analyse du résultat ne dépende pas du transport.
    /// </summary>
    private string BuildWinRmProbe(WinRmTransportMode transport) => transport switch
    {
        WinRmTransportMode.Https =>
            "Test-WSMan -ComputerName '" + ServerFqdn + "' -UseSSL -Port " + WinRmPorts.Https
            + " -ErrorAction Stop | Select-Object ProductVendor, ProductVersion",

        WinRmTransportMode.Http =>
            "Test-WSMan -ComputerName '" + ServerFqdn + "' -Port " + WinRmPorts.Http
            + " -ErrorAction Stop | Select-Object ProductVendor, ProductVersion",

        // Négociation : HTTPS prioritaire, HTTP en repli. Le repli n'est
        // tenté que si HTTPS n'a rien renvoyé.
        WinRmTransportMode.Auto =>
            "$probe = $null\n" +
            "try { $probe = Test-WSMan -ComputerName '" + ServerFqdn + "' -UseSSL -Port " + WinRmPorts.Https + " -ErrorAction Stop } catch { }\n" +
            "if ($null -eq $probe) {\n" +
            "    try { $probe = Test-WSMan -ComputerName '" + ServerFqdn + "' -Port " + WinRmPorts.Http + " -ErrorAction Stop } catch { throw }\n" +
            "}\n" +
            "$probe | Select-Object ProductVendor, ProductVersion",
    };
}