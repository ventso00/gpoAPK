using System.Text.Json;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.PowerShell;
using Xunit;

namespace ADOps.GpoManager.Tests;

/// <summary>Tests du parsing des résultats JSON produits par PowerShell (Étape 3 du cahier des charges).</summary>
public class PowerShellOutputParserTests
{
    private readonly PowerShellOutputParser _parser = new();

    private const string TwoGposJson = """
        [
          {
            "Id": "31b2f340-016d-11d2-945f-00c04fb984f9",
            "DisplayName": "Default Domain Policy",
            "Description": null,
            "CreationTime": "2024-01-15T09:30:00+01:00",
            "ModificationTime": "2026-09-20T14:05:12+02:00",
            "GpoStatus": "AllSettingsEnabled",
            "DomainName": "ventso.test"
          },
          {
            "Id": "6ac1786c-016d-11d2-945f-00c04fb984f9",
            "DisplayName": "GPO-Workstations",
            "Description": "Configuration des postes clients",
            "CreationTime": "2025-03-02T10:00:00+01:00",
            "ModificationTime": "2026-10-01T08:15:00+02:00",
            "GpoStatus": "UserSettingsDisabled",
            "DomainName": "ventso.test"
          }
        ]
        """;

    [Fact]
    public void ParseGpoList_WithArray_ReturnsAllGpos()
    {
        var gpos = _parser.ParseGpoList(TwoGposJson);
        Assert.Equal(2, gpos.Count);
        Assert.Equal("Default Domain Policy", gpos[0].DisplayName);
        Assert.Equal("GPO-Workstations", gpos[1].DisplayName);
    }

    [Fact]
    public void ParseGpoList_MapsAllProperties()
    {
        var gpo = _parser.ParseGpoList(TwoGposJson)[0];
        Assert.Equal(Guid.Parse("31b2f340-016d-11d2-945f-00c04fb984f9"), gpo.Id);
        Assert.Equal("Default Domain Policy", gpo.DisplayName);
        Assert.Null(gpo.Description);
        Assert.Equal(new DateTimeOffset(2024, 1, 15, 9, 30, 0, TimeSpan.FromHours(1)), gpo.CreationTime);
        Assert.Equal(new DateTimeOffset(2026, 9, 20, 14, 5, 12, TimeSpan.FromHours(2)), gpo.ModificationTime);
        Assert.Equal(GpoStatus.AllSettingsEnabled, gpo.Status);
        Assert.Equal("ventso.test", gpo.DomainName);
    }

    [Fact]
    public void ParseGpoList_MapsGpoStatusVariants()
    {
        var json = """
            [
              {"Id":"11111111-1111-1111-1111-111111111111","DisplayName":"A","GpoStatus":"AllSettingsEnabled"},
              {"Id":"22222222-2222-2222-2222-222222222222","DisplayName":"B","GpoStatus":"ComputerSettingsDisabled"},
              {"Id":"33333333-3333-3333-3333-333333333333","DisplayName":"C","GpoStatus":"UserSettingsDisabled"},
              {"Id":"44444444-4444-4444-4444-444444444444","DisplayName":"D","GpoStatus":"AllSettingsDisabled"},
              {"Id":"55555555-5555-5555-5555-555555555555","DisplayName":"E","GpoStatus":"SomethingElse"}
            ]
            """;
        var gpos = _parser.ParseGpoList(json);
        Assert.Equal(GpoStatus.AllSettingsEnabled, gpos[0].Status);
        Assert.Equal(GpoStatus.ComputerSettingsDisabled, gpos[1].Status);
        Assert.Equal(GpoStatus.UserSettingsDisabled, gpos[2].Status);
        Assert.Equal(GpoStatus.AllSettingsDisabled, gpos[3].Status);
        Assert.Equal(GpoStatus.Unknown, gpos[4].Status);
    }

    [Fact]
    public void ParseGpoList_WithSingleObject_WrapsInSingleItemList()
    {
        // ConvertTo-Json produit un objet (et non un tableau) quand il n'y a qu'un seul élément.
        var single = """{"Id":"31b2f340-016d-11d2-945f-00c04fb984f9","DisplayName":"GPO-Users","GpoStatus":"Unknown"}""";
        var gpos = _parser.ParseGpoList(single);
        var gpo = Assert.Single(gpos);
        Assert.Equal("GPO-Users", gpo.DisplayName);
        Assert.Equal(GpoStatus.Unknown, gpo.Status);
    }

    [Fact]
    public void ParseGpoList_WithEmptyOutput_ReturnsEmptyList()
    {
        // Aucune GPO dans le domaine : sortie vide, liste vide, PAS une erreur (contrainte n°5).
        Assert.Empty(_parser.ParseGpoList(string.Empty));
        Assert.Empty(_parser.ParseGpoList("   "));
    }

    [Fact]
    public void ParseGpoList_WithMalformedJson_ReturnsEmptyListInsteadOfThrowing()
    {
        // Une sortie illisible ne doit pas remonter en exception jusqu'à l'écran :
        // elle est traitée comme un résultat vide, au même titre qu'une sortie vide.
        Assert.Empty(_parser.ParseGpoList("{not json"));
        Assert.Empty(_parser.ParseGpoList("<Objs Version=\"1.1.0.1\"></Objs>"));
    }

    // --- Robustesse aux sorties polluées (WinRM, cmdlets natifs) ---

    [Fact]
    public void ParseGpoList_WithClixmlWrapper_StillParsesTheJson()
    {
        // Invoke-Command sérialise en CLIXML et peut préfixer des traces :
        // le JSON utile doit être extrait malgré l'enveloppe.
        var polluted = "<Objs Version=\"1.1.0.1\" xmlns=\"http://schemas.microsoft.com/powershell/mic/command/2004/10\">"
            + "<S S=\"Progress\">0</S>"
            + TwoGposJson
            + "</Objs>";

        var gpos = _parser.ParseGpoList(polluted);
        Assert.Equal(2, gpos.Count);
        Assert.Equal("Default Domain Policy", gpos[0].DisplayName);
    }

    [Fact]
    public void ParseRaw_WithLeadingNoise_ExtractsJson()
    {
        // Bannière de cmdlet natif (gpresult, Add-WindowsCapability) avant le JSON.
        var element = _parser.ParseRaw(
            "L'operation a reussi.\r\n\r\n{\"Name\":\"ad-ds01\",\"IPAddress\":\"192.168.1.10\"}\r\n");

        Assert.NotNull(element);
        Assert.Equal("192.168.1.10", element.Value.GetProperty("IPAddress").GetString());
    }

    [Fact]
    public void ParseRaw_WithOnlyNoise_ReturnsNullWithoutThrowing()
    {
        Assert.Null(_parser.ParseRaw("Progress: 0 of 10"));
        Assert.Null(_parser.ParseRaw("avertissement sans donnée exploitable"));
    }

    [Fact]
    public void ParseModules_WithPollutedOutput_ReturnsModules()
    {
        var polluted = "WARNING: something\r\n[{\"Name\":\"GroupPolicy\",\"Version\":\"1.0.0.0\"}]";
        var modules = _parser.ParseModules(polluted);
        Assert.Equal("GroupPolicy", Assert.Single(modules).Name);
    }

    [Fact]
    public void ParseScalarString_WithNumericJson_ReturnsRawText()
    {
        // Count d'un @{} sérialisé par PowerShell 5.1 peut ne pas être quoted.
        Assert.Equal("3", _parser.ParseScalarString("3"));
    }

    [Fact]
    public void ParseModules_WithArray_ReturnsModules()
    {
        var json = """[{"Name":"GroupPolicy","Version":"1.0.0.0"},{"Name":"ActiveDirectory","Version":"1.0.2.0"}]""";
        var modules = _parser.ParseModules(json);
        Assert.Equal(2, modules.Count);
        Assert.Equal("GroupPolicy", modules[0].Name);
        Assert.Equal("1.0.0.0", modules[0].Version);
        Assert.Equal("ActiveDirectory", modules[1].Name);
    }

    [Fact]
    public void ParseModules_WithEmptyOutput_ReturnsEmptyList()
    {
        Assert.Empty(_parser.ParseModules(string.Empty));
    }

    [Fact]
    public void ParseScalarString_ReturnsUnescapedUsername()
    {
        // $env:USERNAME retourné par Invoke-Command est transporté comme chaîne JSON.
        var username = _parser.ParseScalarString(@"""VENTSO\\administrateur""");
        Assert.Equal("VENTSO\\administrateur", username);
    }

    [Fact]
    public void ParseScalarString_WithEmptyOutput_ReturnsNull()
    {
        Assert.Null(_parser.ParseScalarString(string.Empty));
    }

    [Fact]
    public void ParseRaw_WithValidJson_ReturnsElement()
    {
        var element = _parser.ParseRaw("""{"Name":"ad-ds01","IPAddress":"192.168.1.10"}""");
        Assert.NotNull(element);
        Assert.Equal("192.168.1.10", element.Value.GetProperty("IPAddress").GetString());
    }

    [Fact]
    public void ParseRaw_WithEmptyOutput_ReturnsNull()
    {
        Assert.Null(_parser.ParseRaw(string.Empty));
    }
}
