using ADOps.GpoManager.Core.Network;
using Xunit;

namespace ADOps.GpoManager.Tests;

/// <summary>Localisation du contrôleur de domaine (conversion IP → FQDN).</summary>
public sealed class DcLocatorTests
{
    [Fact]
    public void NormalizeDcName_StripsUncPrefix()
    {
        Assert.Equal("VMST-DC01.sarytany.local", DcLocator.NormalizeDcName(@"\\VMST-DC01.sarytany.local"));
        Assert.Equal("DC01.contoso.local", DcLocator.NormalizeDcName("  DC01.contoso.local "));
    }

    [Fact]
    public void MatchByAddress_RejectsNonIpInput()
    {
        Assert.Null(DcLocator.MatchByAddress("VMST-DC01.sarytany.local", "sarytany.local"));
        Assert.Null(DcLocator.MatchByAddress("not-an-ip", "sarytany.local"));
        Assert.Null(DcLocator.MatchByAddress(string.Empty, "sarytany.local"));
        Assert.Null(DcLocator.MatchByAddress(null, "sarytany.local"));
    }

    [Fact]
    public void MatchByAddress_ReturnsNullWithoutDomain()
    {
        // Aucune découverte possible sans domaine : pas d'appel réseau.
        Assert.Null(DcLocator.MatchByAddress("192.168.1.214", null));
        Assert.Null(DcLocator.MatchByAddress("192.168.1.214", "   "));
        Assert.Null(DcLocator.Discover(null));
        Assert.Null(DcLocator.Discover("  "));
    }

    [Fact]
    public void MatchByAddress_RoundTripsDiscoveredAddress_OnDomainJoinedMachine()
    {
        // Suffixe DNS primaire du poste (vide ou NetBIOS sur un poste non joint).
        var dnsDomain = System.Net.NetworkInformation.IPGlobalProperties
            .GetIPGlobalProperties().DomainName;
        if (string.IsNullOrWhiteSpace(dnsDomain) || !dnsDomain.Contains('.'))
        {
            return;
        }

        var dc = DcLocator.Discover(dnsDomain);
        if (dc is null)
        {
            return;
        }

        Assert.Contains(".", dc.Fqdn);

        // L'adresse du contrôleur (une IP) doit retrouver son FQDN.
        if (System.Net.IPAddress.TryParse(dc.Address, out _))
        {
            Assert.Equal(dc.Fqdn, DcLocator.MatchByAddress(dc.Address, dnsDomain));
        }
    }
}
