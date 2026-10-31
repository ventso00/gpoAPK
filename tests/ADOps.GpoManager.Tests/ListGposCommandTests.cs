using ADOps.GpoManager.Core.Commands;
using Xunit;

namespace ADOps.GpoManager.Tests;

/// <summary>Tests des commandes PowerShell typées : le script généré ne doit jamais contenir de saisie libre.</summary>
public class ListGposCommandTests
{
    [Fact]
    public void ListGposCommand_UsesGetGpoAll()
    {
        var command = new ListGposCommand();
        Assert.Contains("Get-GPO -All", command.ScriptBody);
        Assert.Contains("Import-Module GroupPolicy", command.ScriptBody);
    }

    [Fact]
    public void ListGposCommand_BuildScript_WrapsWithStrictErrorHandlingAndJson()
    {
        var script = new ListGposCommand().BuildScript();
        Assert.Contains("$ErrorActionPreference = 'Stop'", script);
        Assert.Contains("ConvertTo-Json -Depth 10 -Compress", script);
        Assert.Contains("Get-GPO -All", script);
        Assert.Contains("[Console]::OutputEncoding = [System.Text.Encoding]::UTF8", script);
    }

    [Fact]
    public void ListGposCommand_HasExplicitTimeout()
    {
        Assert.True(new ListGposCommand().Timeout > TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void TestConnectionCommand_ValidatesFqdn()
    {
        Assert.Throws<ArgumentException>(() => new TestConnectionCommand(""));
        Assert.Throws<ArgumentException>(() => new TestConnectionCommand("bad host!"));
    }

    [Fact]
    public void TestConnectionCommand_NormalizesFqdn()
    {
        var command = new TestConnectionCommand(" AD-DS01.VENTSO.TEST. ");
        Assert.Equal("ad-ds01.ventso.test", command.ServerFqdn);
    }

    [Fact]
    public void TestConnectionCommand_ContainsAllHealthCheckSteps()
    {
        var command = new TestConnectionCommand("ad-ds01.ventso.test");

        Assert.Contains("Resolve-DnsName -Name 'ad-ds01.ventso.test'", command.DnsResolution.ScriptBody);
        // Le transport par défaut négocie : les deux canaux sont sondés,
        // la cible restant identifiée par son FQDN dans les deux cas.
        Assert.Contains("Test-WSMan -ComputerName 'ad-ds01.ventso.test'", command.WinRmService.ScriptBody);
        Assert.Contains("Invoke-Command -ComputerName 'ad-ds01.ventso.test'", command.Authentication.ScriptBody);
        Assert.Contains("Get-Module -ListAvailable", command.LocalModules.ScriptBody);

        Assert.Equal("DnsResolution", command.DnsResolution.Name);
        Assert.Equal("WinRmService", command.WinRmService.Name);
        Assert.Equal("Authentication", command.Authentication.Name);
        Assert.Equal("LocalModules", command.LocalModules.Name);
    }

    [Fact]
    public void TestConnectionCommand_StepTimeoutsAreBounded()
    {
        var command = new TestConnectionCommand("ad-ds01.ventso.test");
        Assert.True(command.DnsResolution.Timeout <= TimeSpan.FromSeconds(20));
        Assert.True(command.WinRmService.Timeout <= TimeSpan.FromSeconds(90));
        Assert.True(command.Authentication.Timeout <= TimeSpan.FromSeconds(60));
        Assert.True(command.LocalModules.Timeout <= TimeSpan.FromSeconds(30));
    }
}
