using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Validation;
using Xunit;

namespace ADOps.GpoManager.Tests;

/// <summary>Tests de validation des entrées (FQDN, domaine) — Étape 3 du cahier des charges.</summary>
public class DomainValidatorTests
{
    [Theory]
    [InlineData("ad-ds01.ventso.test")]
    [InlineData("AD-DS01.VENTSO.TEST")]
    [InlineData(" ventso.test ")]
    [InlineData("dc01")]
    [InlineData("192.168.1.10")]
    public void ValidateServerFqdn_AcceptsValidValues(string value)
    {
        var (isValid, error) = DomainValidator.ValidateServerFqdn(value);
        Assert.True(isValid, error ?? "La valeur devrait être valide.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("ad ds01")]
    [InlineData("ad-ds01.ventso..test")]
    [InlineData("-ad-ds01.ventso.test")]
    [InlineData("ad-ds01-.ventso.test")]
    [InlineData("ad_ds01.ventso.test")]
    [InlineData("ad-ds01.ventso.test/")]
    [InlineData("999.999.999.999")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.test")]
    public void ValidateServerFqdn_RejectsInvalidValues(string? value)
    {
        var (isValid, error) = DomainValidator.ValidateServerFqdn(value);
        Assert.False(isValid);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("ventso.test")]
    [InlineData("sub.domain.example.com")]
    public void ValidateDomainName_AcceptsValidDomains(string value)
    {
        var (isValid, error) = DomainValidator.ValidateDomainName(value);
        Assert.True(isValid, error ?? "Le domaine devrait être valide.");
    }

    [Fact]
    public void ValidateDomainName_RequiresAtLeastTwoLabels()
    {
        var (isValid, error) = DomainValidator.ValidateDomainName("ventso");
        Assert.False(isValid);
        Assert.NotNull(error);
    }

    [Fact]
    public void ValidateDomainName_RejectsEmptyValue()
    {
        var (isValid, error) = DomainValidator.ValidateDomainName("   ");
        Assert.False(isValid);
        Assert.NotNull(error);
    }

    [Fact]
    public void NormalizeFqdn_TrimsLowercasesAndRemovesTrailingDot()
    {
        Assert.Equal("ad-ds01.ventso.test", DomainValidator.NormalizeFqdn(" AD-DS01.VENTSO.TEST. "));
    }

    [Fact]
    public void DomainContext_RejectsInvalidServer()
    {
        Assert.Throws<ArgumentException>(() =>
            new DomainContext("bad host!", "ventso.test", null, ExecutorMode.Local, "user", DateTime.UtcNow));
    }

    [Fact]
    public void DomainContext_RejectsSingleLabelDomain()
    {
        Assert.Throws<ArgumentException>(() =>
            new DomainContext("ad-ds01.ventso.test", "ventso", null, ExecutorMode.Local, "user", DateTime.UtcNow));
    }

    [Fact]
    public void DomainContext_NormalizesAndStoresValues()
    {
        var context = new DomainContext(" AD-DS01.VENTSO.TEST. ", "VENTSO.TEST", " VENTSO ", ExecutorMode.Remote, "VENTSO\\admin", DateTime.UtcNow);
        Assert.Equal("ad-ds01.ventso.test", context.ServerFqdn);
        Assert.Equal("ventso.test", context.DomainFqdn);
        Assert.Equal("VENTSO", context.DomainNetbios);
        Assert.Equal(ExecutorMode.Remote, context.ExecutorMode);
        Assert.Equal("VENTSO\\admin", context.UserName);
    }

    [Fact]
    public void DomainContext_FallsBackToEnvironmentUser()
    {
        var context = new DomainContext("ad-ds01.ventso.test", "ventso.test", null, ExecutorMode.Local, string.Empty, DateTime.UtcNow);
        Assert.Equal(Environment.UserName, context.UserName);
    }
}
