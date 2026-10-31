using ADOps.GpoManager.ActiveDirectory;
using ADOps.GpoManager.Core.Commands;
using ADOps.GpoManager.Infrastructure.Storage;
using Xunit;

namespace ADOps.GpoManager.Tests;

/// <summary>
/// Verrouille la détection d'IP du poste : l'adresse doit venir d'AD DS
/// (attribut IPv4Address), être transportée jusqu'au cache local et primer
/// sur « IP inconnue » dès qu'une source la fournit.
/// </summary>
public class ComputerIpAddressTests
{
    [Fact]
    public void ListComputersCommand_ProjectsTheAdIpv4Attribute()
    {
        var script = new ListComputersCommand().ScriptBody;

        Assert.Contains("IPv4Address", script, StringComparison.Ordinal);
    }

    [Fact]
    public void AdOutputParser_ReadsTheDeclaredIpv4Address()
    {
        const string json = """
            [{"Name":"DESKTOP-NRJBK5E",
              "DistinguishedName":"CN=DESKTOP-NRJBK5E,OU=Ateliers,DC=sarytany,DC=local",
              "SamAccountName":"DESKTOP-NRJBK5E$",
              "OperatingSystem":"Windows 11 Pro",
              "Enabled":true,
              "IPv4Address":"192.168.5.31"}]
            """;

        var computer = Assert.Single(new AdOutputParser().ParseComputers(json));

        Assert.Equal("192.168.5.31", computer.IpAddress);
    }

    [Fact]
    public void AdOutputParser_ToleratesAComputerWithoutAnDeclaredAddress()
    {
        const string json = """
            [{"Name":"POSTE-2","DistinguishedName":"CN=POSTE-2,DC=sarytany,DC=local","Enabled":true}]
            """;

        var computer = Assert.Single(new AdOutputParser().ParseComputers(json));

        Assert.Null(computer.IpAddress);
    }

    [Fact]
    public void ComputerRow_PrefersVerifiedDnsOverTheAdDeclaration()
    {
        var row = new ComputerRow { DnsIpAddress = "192.168.5.60", AdIpAddress = "192.168.5.31" };

        Assert.Equal("192.168.5.60", row.EffectiveIpAddress);
    }

    [Fact]
    public void ComputerRow_FallsBackToTheAdDeclarationWhenDnsIsMissing()
    {
        // Cas du contrôle de disponibilité en échec : dns_ip est upserté à NULL,
        // l'objet AD DS garde néanmoins l'adresse déclarative.
        var row = new ComputerRow { DnsIpAddress = null, AdIpAddress = "192.168.5.31" };

        Assert.Equal("192.168.5.31", row.EffectiveIpAddress);
    }

    [Fact]
    public void ComputerRow_ReportsNoAddressOnlyWhenBothSourcesAreEmpty()
    {
        Assert.Null(new ComputerRow { DnsIpAddress = "   ", AdIpAddress = string.Empty }.EffectiveIpAddress);
    }
}
