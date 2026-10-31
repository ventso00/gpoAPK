using ADOps.GpoManager.Core.Commands;
using ADOps.GpoManager.Core.Security;
using ADOps.GpoManager.PowerShell;
using ADOps.GpoManager.PowerShell.Host;
using Xunit;

namespace ADOps.GpoManager.Tests;

/// <summary>
/// Verrouille le correctif « #< CLIXML » : le flux d'erreur sérialisé doit être
/// décodé avant d'atteindre l'interface, et les scripts natifs (gpresult,
/// gpupdate) ne doivent plus fabriquer eux-mêmes la NativeCommandError.
/// </summary>
public class ClixmlErrorHandlingTests
{
    private const string ClixmlEnvelope =
        "#< CLIXML\r\n" +
        "<Objs Version=\"1.1.0.1\" xmlns=\"http://schemas.microsoft.com/powershell/2004/04\">" +
        "<S S=\"Error\">Le client WinRM ne peut pas se connecter à la destination spécifiée. (0x800706BA)_x000D__x000A_</S>" +
        "<S S=\"Progress\">1234</S>" +
        "</Objs>";

    [Fact]
    public void Decode_ExtractsTheRealMessage_AndDropsTheEnvelope()
    {
        var decoded = ClixmlErrorDecoder.Decode(ClixmlEnvelope);

        Assert.DoesNotContain("#< CLIXML", decoded, StringComparison.Ordinal);
        Assert.DoesNotContain("<Objs", decoded, StringComparison.Ordinal);
        Assert.DoesNotContain("Progress", decoded, StringComparison.Ordinal);
        Assert.Contains("Le client WinRM ne peut pas se connecter", decoded, StringComparison.Ordinal);
    }

    [Fact]
    public void Decode_LeavesPlainStderrUntouched()
    {
        const string plain = "Get-GPO : The term 'Get-GPO' is not recognized.";

        Assert.Equal(plain, ClixmlErrorDecoder.Decode(plain));
        Assert.Equal(string.Empty, ClixmlErrorDecoder.Decode(null));
    }

    [Fact]
    public void Decode_UnescapesXmlEntities()
    {
        var envelope = "#< CLIXML\r\n<Objs><S S=\"Error\">gpresult a &lt;&gt; &quot;échoué&quot;_x000D__x000A_</S></Objs>";

        var decoded = ClixmlErrorDecoder.Decode(envelope);

        Assert.Contains("gpresult a <> \"échoué\"", decoded, StringComparison.Ordinal);
    }

    [Fact]
    public void Interpreter_OnDecodedWinRmEnvelope_ReturnsWinRmRule_NotRawClixml()
    {
        var interpreter = new ErrorInterpreter();
        var result = new PowerShellExecutionResult(
            ExitCode: 1,
            StandardOutput: string.Empty,
            StandardError: ClixmlErrorDecoder.Decode(ClixmlEnvelope),
            TimedOut: false,
            Duration: TimeSpan.FromSeconds(2));

        var error = interpreter.Interpret(result, "Vérification des GPO (gpresult)");

        Assert.Contains("WinRM", error.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#< CLIXML", error.ToDisplayString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Interpreter_OnConnectivityProbeFailure_ExplainsWinRmPorts()
    {
        var interpreter = new ErrorInterpreter();
        var probe = new WinRmConnectivityProbe.ProbeResult(
            Resolved: true,
            IpAddress: "192.168.5.42",
            HttpReachable: false,
            HttpsReachable: false);
        var result = WinRmConnectivityProbe.BuildUnreachableResult(
            "DESKTOP-NRJBK5E.sarytany.local", probe, WinRmTransportMode.Http, TimeSpan.FromMilliseconds(40));

        var error = interpreter.Interpret(result, "Vérification des GPO (gpresult)");

        Assert.Contains("WinRM", error.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Enable-PSRemoting", error.Remediation ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Interpreter_OnUnresolvedName_ExplainsDns()
    {
        var interpreter = new ErrorInterpreter();
        var probe = new WinRmConnectivityProbe.ProbeResult(false, null, false, false);
        var result = WinRmConnectivityProbe.BuildUnreachableResult(
            "DESKTOP-NRJBK5E.sarytany.local", probe, WinRmTransportMode.Auto, TimeSpan.FromMilliseconds(40));

        var error = interpreter.Interpret(result, "Vérification des GPO (gpresult)");

        Assert.Contains("DNS", error.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("registerdns", error.Remediation ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DESKTOP-NRJBK5E", error.RawError ?? string.Empty, StringComparison.Ordinal);
    }

    // --- Scripts : plus aucune redirection 2>&1 sur une commande native ---

    [Fact]
    public void GetGpResultScript_DoNotPipeStderrIntoThePipeline()
    {
        AssertScriptIsClixmlSafe(new GetGpResultCommand("DESKTOP-NRJBK5E").BuildScript());
    }

    [Fact]
    public void InvokeGpUpdateScript_DoNotPipeStderrIntoThePipeline()
    {
        AssertScriptIsClixmlSafe(new InvokeGpUpdateCommand("DESKTOP-NRJBK5E").BuildScript());
    }

    [Fact]
    public void GetGpoResultantSetScript_DoNotPipeStderrIntoThePipeline()
    {
        AssertScriptIsClixmlSafe(new GetGpoResultantSetCommand("DESKTOP-NRJBK5E.sarytany.local").BuildScript());
    }

    private static void AssertScriptIsClixmlSafe(string script)
    {
        Assert.DoesNotContain("2>&1", script, StringComparison.Ordinal);
        Assert.Contains("RedirectStandardError", script, StringComparison.Ordinal);
        // La valeur de sortie du binaire seul décide du succès.
        Assert.Contains("ExitCode", script, StringComparison.Ordinal);
    }

    [Fact]
    public void GetGpResult_ReturnsRawReports_TheApplicationParsesThem()
    {
        var script = new GetGpResultCommand("DESKTOP-NRJBK5E").ScriptBody;

        // Le script ne devine plus les noms : il livre le rapport texte et, en repli,
        // le rapport XML. La sélection des GPO appliquées est testée côté C#.
        Assert.Contains("RawOutput", script, StringComparison.Ordinal);
        Assert.Contains("XmlReport", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Objet de stratégie de groupe", script, StringComparison.Ordinal);
        // UTF-8 strict d'abord : la page OEM ne sert que de repli.
        Assert.Contains("UTF8Encoding($false, $true)", script, StringComparison.Ordinal);
        Assert.Contains("OEMCP", script, StringComparison.Ordinal);
    }

    [Fact]
    public void GetGpResult_CleansUpItsTempFiles()
    {
        var script = new GetGpResultCommand("DESKTOP-NRJBK5E").ScriptBody;

        Assert.Contains("finally", script, StringComparison.Ordinal);
        Assert.Contains("Remove-Item", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ProbeResult_KnowsWhenAnyPortIsReachable()
    {
        Assert.True(new WinRmConnectivityProbe.ProbeResult(true, "10.0.0.1", false, true).AnyPortReachable);
        Assert.False(new WinRmConnectivityProbe.ProbeResult(true, "10.0.0.1", false, false).AnyPortReachable);
    }

    [Fact]
    public void UnreachableResult_IsAnExplicitFailure_NotAZeroExit()
    {
        var probe = new WinRmConnectivityProbe.ProbeResult(true, "192.168.5.42", false, false);

        var result = WinRmConnectivityProbe.BuildUnreachableResult(
            "poste.sarytany.local", probe, WinRmTransportMode.Http, TimeSpan.FromMilliseconds(12));

        Assert.Equal(1, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.DoesNotContain("#< CLIXML", result.StandardError, StringComparison.Ordinal);
    }
}
