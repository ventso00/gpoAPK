using ADOps.GpoManager.PowerShell;
using Xunit;

namespace ADOps.GpoManager.Tests;

/// <summary>
/// Tests du lecteur de rapports gpresult. Les échantillons sont des sorties réelles
/// de gpresult.exe (console anglaise) et leur équivalent français : c'est la structure
/// même de ces sorties qui rendait l'ancienne extraction par regex inopérante.
/// </summary>
public class GpResultReportParserTests
{
    /// <summary>Rapport /r capturé sur un contrôleur de domaine du domaine de test.</summary>
    private const string EnglishReport = """

        Microsoft (R) Windows (R) Operating System Group Policy Result tool v2.0
        c Microsoft Corporation. All rights reserved.

        Created on 10/30/2026 at 3:21:13 AM

        RSOP data for SARYTANY\Administrator on VENTSO : Logging Mode
        --------------------------------------------------------------

        OS Configuration:            Primary Domain Controller
        Connected over a slow link?: No


        COMPUTER SETTINGS
        ------------------
            CN=VENTSO,OU=Domain Controllers,DC=sarytany,DC=local
            Last time Group Policy was applied: 10/30/2026 at 3:18:36 AM
            Domain Name:                        SARYTANY

            Applied Group Policy Objects
            -----------------------------
                Default Domain Controllers Policy
                Default Domain Policy
                Default Domain Policy

            The following GPOs were not applied because they were filtered out
            -------------------------------------------------------------------
                Local Group Policy
                    Filtering:  Not Applied (Empty)

            The computer is a part of the following security groups
            -------------------------------------------------------
                BUILTIN\Administrators
                VENTSO$

        USER SETTINGS
        --------------
            CN=Administrator,CN=Users,DC=sarytany,DC=local

            Applied Group Policy Objects
            -----------------------------
                N/A

            The following GPOs were not applied because they were filtered out
            -------------------------------------------------------------------
                Local Group Policy
                    Filtering:  Not Applied (Empty)
        """;

    private const string FrenchReport = """
        Paramètres de l'ordinateur
        --------------------------
            CN=POSTE-01,OU=Station de travail,DC=contoso,DC=local

            Stratégies de groupe appliquées
            --------------------------------
                Restrictions du navigateur
                GPO Serveur de fichiers

            Les stratégies de groupe suivantes n'ont pas été appliquées car elles ont été filtrées
            --------------------------------------------------------------------------------------
                Stratégie de groupe locale
                    Filtrage : Non appliquée (vide)

            L'ordinateur fait partie des groupes de sécurité suivants
            ---------------------------------------------------------
                Tout le monde
        """;

    private const string RealShapeXml = """
        <?xml version="1.0" encoding="UTF-16"?>
        <Rsop xmlns="http://www.microsoft.com/GroupPolicy/Rsop">
          <ReadTime>10/30/2026 03:21:13</ReadTime>
          <DataType>Logging</DataType>
          <ComputerResults>
            <Version>2.0</Version>
            <Name>VENTSO</Name>
            <Domain>sarytany.local</Domain>
            <GPO>
              <Name>Default Domain Controllers Policy</Name>
              <Path>{6AC1786C-016F-11D2-945F-00C04fB984F9}sarytany.local</Path>
              <Enabled>true</Enabled>
              <IsValid>true</IsValid>
              <FilterAllowed>true</FilterAllowed>
              <AccessDenied>false</AccessDenied>
            </GPO>
            <GPO>
              <Name>Local Group Policy</Name>
              <Path>LocalGPO</Path>
              <Enabled>true</Enabled>
              <IsValid>true</IsValid>
              <FilterAllowed>true</FilterAllowed>
              <AccessDenied>true</AccessDenied>
            </GPO>
            <GPO>
              <Name>Default Domain Policy</Name>
              <Path>{31B2F340-016D-11D2-945F-00C04FB984F9}sarytany.local</Path>
              <Enabled>true</Enabled>
              <IsValid>true</IsValid>
              <FilterAllowed>true</FilterAllowed>
              <AccessDenied>false</AccessDenied>
            </GPO>
            <SecurityGroup>
              <SID xmlns="http://www.microsoft.com/GroupPolicy/Types">S-1-5-32-544</SID>
              <Name xmlns="http://www.microsoft.com/GroupPolicy/Types">BUILTIN\Administrators</Name>
            </SecurityGroup>
            <ExtensionData>
              <Extension>
                <Account xmlns="http://www.microsoft.com/GroupPolicy/Settings/Accounts">
                  <GPO><Identifier>X</Identifier></GPO>
                </Account>
              </Extension>
            </ExtensionData>
          </ComputerResults>
        </Rsop>
        """;

    [Fact]
    public void ParseAppliedGpoNames_RealEnglishReport_ReturnsAppliedListWithoutFilteredOutEntries()
    {
        var names = GpResultReportParser.ParseAppliedGpoNames(EnglishReport);

        Assert.Equal(
            new[] { "Default Domain Controllers Policy", "Default Domain Policy" },
            names);
        Assert.DoesNotContain("Local Group Policy", names);
        Assert.DoesNotContain("N/A", names);
        Assert.DoesNotContain("BUILTIN\\Administrators", names);
    }

    [Fact]
    public void ParseAppliedGpoNames_FrenchReport_ReturnsAppliedList()
    {
        var names = GpResultReportParser.ParseAppliedGpoNames(FrenchReport);

        Assert.Equal(new[] { "Restrictions du navigateur", "GPO Serveur de fichiers" }, names);
        Assert.DoesNotContain("Stratégie de groupe locale", names);
    }

    [Fact]
    public void ParseAppliedGpoNames_AppliedSectionWithNoGpo_ReturnsEmptyList()
    {
        var report = """
            COMPUTER SETTINGS
            ------------------
                Applied Group Policy Objects
                -----------------------------
                    N/A

                The following GPOs were not applied because they were filtered out
            """;

        Assert.Empty(GpResultReportParser.ParseAppliedGpoNames(report));
    }

    [Fact]
    public void ParseAppliedGpoNamesFromXml_RealNamespaces_IgnoresDeniedAndSettingNodes()
    {
        var names = GpResultReportParser.ParseAppliedGpoNamesFromXml(RealShapeXml);

        Assert.Equal(new[] { "Default Domain Controllers Policy", "Default Domain Policy" }, names);
    }

    [Fact]
    public void ParseAppliedGpoNamesFromXml_UnreadableXml_ReturnsEmptyList()
    {
        Assert.Empty(GpResultReportParser.ParseAppliedGpoNamesFromXml("<Rsop><unclosed>"));
    }

    [Fact]
    public void ExtractXmlReport_JsonStringWrappedReport_IsUnwrapped()
    {
        var stdout = System.Text.Json.JsonSerializer.Serialize(RealShapeXml);

        var report = GpResultReportParser.ExtractXmlReport(stdout);

        Assert.NotNull(report);
        Assert.StartsWith("<?xml", report, StringComparison.Ordinal);
        Assert.EndsWith("</Rsop>", report, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractXmlReport_BareReportWithTrailingNoise_IsIsolated()
    {
        var stdout = RealShapeXml + "\n<Objs Version=\"1.1.0.1\" xmlns=\"http://schemas.microsoft.com/powershell/2004/04\"></Objs>";

        var report = GpResultReportParser.ExtractXmlReport(stdout);

        Assert.NotNull(report);
        Assert.DoesNotContain("<Objs", report, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractXmlReport_NoReport_ReturnsNull()
    {
        Assert.Null(GpResultReportParser.ExtractXmlReport("gpresult a echoue sur ce poste (code de sortie 1)."));
    }

    [Fact]
    public void FromCommandOutput_JsonReport_ReturnsAppliedNames()
    {
        var stdout = System.Text.Json.JsonSerializer.Serialize(new
        {
            Computer = "VENTSO",
            ExitCode = 0,
            RawOutput = EnglishReport,
            XmlReport = string.Empty,
            Timestamp = "2026-10-30T01:21:13.0000000Z",
        });

        var names = GpResultReportParser.FromCommandOutput(stdout);

        Assert.Contains("Default Domain Controllers Policy", names);
        Assert.Contains("Default Domain Policy", names);
        Assert.Equal(2, names.Count);
    }

    [Fact]
    public void FromCommandOutput_EmptyTextWithXml_FallsBackToTheXmlReport()
    {
        var stdout = System.Text.Json.JsonSerializer.Serialize(new
        {
            Computer = "VENTSO",
            ExitCode = 0,
            RawOutput = string.Empty,
            XmlReport = RealShapeXml,
            Timestamp = "2026-10-30T01:21:13.0000000Z",
        });

        var names = GpResultReportParser.FromCommandOutput(stdout);

        Assert.Equal(new[] { "Default Domain Controllers Policy", "Default Domain Policy" }, names);
    }

    [Fact]
    public void FromCommandOutput_PollutedOutput_IsStillRead()
    {
        // Bandeau natif devant le JSON compact, comme peut le renvoyer un canal WinRM.
        var stdout = "Windows (R) Windows (R) Group Policy Result tool v2.0\n"
            + System.Text.Json.JsonSerializer.Serialize(new
            {
                Computer = "VENTSO",
                ExitCode = 0,
                RawOutput = "    Applied Group Policy Objects\n    -----------------------------\n        GPO Bureau\n",
                XmlReport = string.Empty,
            });

        var names = GpResultReportParser.FromCommandOutput(stdout);

        Assert.Equal(new[] { "GPO Bureau" }, names);
    }
}
