using ADOps.GpoManager.Core.Commands;
using ADOps.GpoManager.Core.Security;
using ADOps.GpoManager.PowerShell;
using ADOps.GpoManager.PowerShell.Host;
using Xunit;

namespace ADOps.GpoManager.Tests;

/// <summary>
/// Tests du canal WinRM (contrainte n°1 de l'adaptation à une plateforme de
/// test VMware : le contrôleur n'écoute que sur HTTP 5985 et son certificat
/// HTTPS est auto-signé).
///
/// Invariant central : le repli vers le clair n'est accordé QUE sur un échec
/// imputable au canal chiffré. Un échec métier (droits insuffisants, commande
/// refusée, GPO introuvable) ne doit jamais être rejoué sur un canal non protégé.
/// </summary>
public class WinRmTransportTests
{
    private static PowerShellExecutionResult Failure(string stderr, int exitCode = 1, bool timedOut = false)
        => new(exitCode, string.Empty, stderr, timedOut, TimeSpan.FromSeconds(1));

    // ---------- Détection du motif de repli ----------

    [Theory]
    [InlineData("The WinRM client cannot process the request. The certificate chain was issued by an authority that is not trusted.")]
    [InlineData("The certificate is not trusted by the machine that runs the process.")]
    [InlineData("Exception from HRESULT: 0x80090312 (The certificate chain was issued by an authority that is not trusted.)")]
    [InlineData("The SSL connection could not be established, see inner exception.")]
    [InlineData("WINRM 0x80072ee7 connection refused")]
    public void FindHttpsFallbackReason_CanalFailure_AuthorisesFallback(string stderr)
    {
        var reason = RemoteExecutor.FindHttpsFallbackReason(Failure(stderr));
        Assert.NotNull(reason);
    }

    [Theory]
    [InlineData("Access is denied. (Exception from HRESULT: 0x80070005 (E_ACCESSDENIED))")]
    [InlineData("Le terme 'Get-GPO' n'est pas reconnu comme nom d'une cmdlet.")]
    [InlineData("New-GPLink : Impossible de trouver un objet avec l'identité spécifiée.")]
    [InlineData("The RPC server is unavailable.")]
    public void FindHttpsFallbackReason_BusinessFailure_RefusesFallback(string stderr)
    {
        // Rejouer ces échecs en clair serait inutile (ils échoueront aussi en HTTP)
        // et ferait transiter des données sur un canal non protégé.
        Assert.Null(RemoteExecutor.FindHttpsFallbackReason(Failure(stderr)));
    }

    [Fact]
    public void FindHttpsFallbackReason_Success_HasNoReason()
    {
        var result = new PowerShellExecutionResult(0, "{}", string.Empty, false, TimeSpan.FromSeconds(1));
        Assert.Null(RemoteExecutor.FindHttpsFallbackReason(result));
    }

    // ---------- Étiquette de transport du résultat ----------

    [Fact]
    public void TransportFallbackOccurred_OnlyWhenFallbackHappened()
    {
        var negotiated = new PowerShellExecutionResult(0, "{}", string.Empty, false, TimeSpan.Zero)
        {
            TransportUsed = WinRmTransportMode.Http,
            TransportFallbackReason = "certificate chain",
        };
        Assert.True(negotiated.TransportFallbackOccurred);

        // HTTP choisi d'emblée par l'administrateur : ce n'est pas un repli.
        var chosen = new PowerShellExecutionResult(0, "{}", string.Empty, false, TimeSpan.Zero)
        {
            TransportUsed = WinRmTransportMode.Http,
        };
        Assert.False(chosen.TransportFallbackOccurred);

        // HTTPS abouti : rien à signaler.
        var secure = new PowerShellExecutionResult(0, "{}", string.Empty, false, TimeSpan.Zero)
        {
            TransportUsed = WinRmTransportMode.Https,
        };
        Assert.False(secure.TransportFallbackOccurred);
    }

    // ---------- Sonde Test-WSMan du Health Check ----------

    [Fact]
    public void TestConnection_Https_UsesSslListenerOnly()
    {
        var script = ScriptOf(new TestConnectionCommand("dc1.lab.local", WinRmTransportMode.Https).WinRmService);
        Assert.Contains("-UseSSL", script);
        Assert.Contains("5986", script);
        Assert.DoesNotContain("-Port 5985", script);
    }

    [Fact]
    public void TestConnection_Http_UsesCleartextListenerOnly()
    {
        var script = ScriptOf(new TestConnectionCommand("dc1.lab.local", WinRmTransportMode.Http).WinRmService);
        Assert.Contains("5985", script);
        Assert.DoesNotContain("-UseSSL", script);
    }

    [Fact]
    public void TestConnection_Auto_ProbesHttpsThenFallsBackToHttp()
    {
        var script = ScriptOf(new TestConnectionCommand("dc1.lab.local", WinRmTransportMode.Auto).WinRmService);

        // HTTPS est sondé en premier...
        Assert.Contains("-UseSSL", script);
        Assert.Contains("5986", script);
        // ...et le repli HTTP n'a lieu que si HTTPS n'a rien donné.
        Assert.Contains("5985", script);
        Assert.Contains("if ($null -eq $probe)", script);
    }

    [Fact]
    public void TestConnection_DefaultTransport_IsNegotiation()
    {
        // Un appel sans transport explicite doit négocier : c'est ce qui rend
        // l'application utilisable sur un contrôleur de test non configuré.
        var command = new TestConnectionCommand("dc1.lab.local");
        var script = ScriptOf(command.WinRmService);
        Assert.Contains("-UseSSL", script);
        Assert.Contains("5985", script);
    }

    // ---------- Compatibilité de la configuration persistée ----------

    [Fact]
    public void SecurityOptions_Default_Negotiates()
    {
        Assert.Equal(WinRmTransportMode.Auto, new SecurityOptions().ResolveWinRmTransport());
    }

    [Fact]
    public void SecurityOptions_LegacyFalseFlag_ForcesHttp()
    {
        // Un settings.json existant avec « RequireWinRmHttps: false » doit
        // conserver le comportement historique plutôt que de revenir à Auto.
        var legacy = new SecurityOptions { RequireWinRmHttps = false };
        Assert.Equal(WinRmTransportMode.Http, legacy.ResolveWinRmTransport());
    }

    [Fact]
    public void SecurityOptions_WithWinRmTransport_KeepsLegacyFlagConsistent()
    {
        var fromAuto = new SecurityOptions().WithWinRmTransport(WinRmTransportMode.Http);
        Assert.Equal(WinRmTransportMode.Http, fromAuto.ResolveWinRmTransport());
        Assert.False(fromAuto.RequireWinRmHttps);

        var fromHttp = fromAuto.WithWinRmTransport(WinRmTransportMode.Https);
        Assert.Equal(WinRmTransportMode.Https, fromHttp.ResolveWinRmTransport());
        Assert.True(fromHttp.RequireWinRmHttps);

        // Le repli vers une valeur héritée incohérente doit rester impossible.
        var back = fromHttp.WithWinRmTransport(WinRmTransportMode.Auto);
        Assert.Equal(WinRmTransportMode.Auto, back.ResolveWinRmTransport());
        Assert.True(back.RequireWinRmHttps);
    }

    [Fact]
    public void SecurityOptions_WithWinRmTransport_PreservesOtherOptions()
    {
        var original = new SecurityOptions
        {
            SessionTimeoutMinutes = 25,
            LogSanitizationEnabled = false,
        };

        var updated = original.WithWinRmTransport(WinRmTransportMode.Http);

        Assert.Equal(25, updated.SessionTimeoutMinutes);
        Assert.False(updated.LogSanitizationEnabled);
    }

    // ---------- Fabrique d'exécuteurs ----------

    [Fact]
    public void ExecutorFactory_Transport_IsAppliedToRemoteExecutors()
    {
        var factory = new PowerShellExecutorFactory();
        Assert.Equal(WinRmTransportMode.Auto, factory.Transport);

        factory.Transport = WinRmTransportMode.Https;
        Assert.Equal(WinRmTransportMode.Https, factory.Transport);

        // Le changement est visible sans recréer la fabrique : c'est ce qui
        // permet au réglage « Paramètres » de s'appliquer sans redémarrage.
        var remote = factory.CreateRemote("dc1.lab.local");
        Assert.IsType<RemoteExecutor>(remote);
    }

    [Fact]
    public void ExecutorFactory_LegacyRequireHttps_MapsToTransport()
    {
        var factory = new PowerShellExecutorFactory { RequireHttps = false };
        Assert.Equal(WinRmTransportMode.Http, factory.Transport);

        factory.RequireHttps = true;
        Assert.Equal(WinRmTransportMode.Https, factory.Transport);
    }

    /// <summary>Corps du script d'une commande, pour assertions sur le texte généré.</summary>
    private static string ScriptOf(PowerShellCommand command) => command.BuildScript();
}

/// <summary>
/// Tests du script complet produit par <see cref="PowerShellCommand.BuildScript"/>.
/// </summary>
/// <remarks>
/// Invariant vérifié ici : le corps de la commande est exécuté dans un
/// scriptblock « &amp; { … } ». La forme antérieure, « $out = ( corps ) », est une
/// erreur de syntaxe PowerShell dès que le corps comporte plus d'une
/// instruction — ce qui est le cas de la quasi-totalité des commandes
/// (Import-Module suivi de Get-GPO, affectation puis sérialisation). Le
/// script produit était donc refusé par l'analyseur avant même toute
/// exécution. Ces tests verrouillent la forme correcte contre une régression.
/// </remarks>
public class PowerShellScriptBuilderTests
{
    public static IEnumerable<object[]> BodiesWithSeveralStatements()
    {
        yield return new object[] { "Import-Module GroupPolicy -ErrorAction Stop\nGet-GPO -All -ErrorAction Stop | Select-Object Id" };
        yield return new object[] { "Import-Module ActiveDirectory -ErrorAction Stop\nGet-ADDomain -ErrorAction Stop | Select-Object Name" };
        yield return new object[] { "Import-Module GroupPolicy -ErrorAction Stop\n$gpos = Get-GPO -All -ErrorAction Stop\n[PSCustomObject]@{ Count = @($gpos).Count }" };
        yield return new object[] { "$a = 1\n$b = 2\n[PSCustomObject]@{ Sum = $a + $b }" };
    }

    [Theory]
    [MemberData(nameof(BodiesWithSeveralStatements))]
    public void BuildScript_MultiStatementBody_UsesInvocationScriptBlock(string body)
    {
        var command = new SimplePowerShellCommand("Test", body);
        var script = command.BuildScript();

        // Un corps multi-instructions ne peut pas être placé entre parenthèses.
        Assert.Contains("$out = & {", script);
        Assert.DoesNotContain("$out = (" + body, script);
    }

    [Fact]
    public void BuildScript_KeepsStrictErrorHandlingAndJsonSerialization()
    {
        var script = new SimplePowerShellCommand("Test", "Get-Date -Format o").BuildScript();

        Assert.Contains("$ErrorActionPreference = 'Stop'", script);
        Assert.Contains("$ProgressPreference = 'SilentlyContinue'", script);
        Assert.Contains("ConvertTo-Json -Depth 10 -Compress", script);
        // L'encodage UTF-8 est nécessaire : les libellés français du projet
        // transitent par cette sortie.
        Assert.Contains("[Console]::OutputEncoding = [System.Text.Encoding]::UTF8", script);
    }

    [Fact]
    public void BuildScript_SingleStatementBody_StillWorks()
    {
        // Un corps simple se comporte à l'identique sous « & { } ».
        var script = new SimplePowerShellCommand("Test", "Get-Date -Format o").BuildScript();
        Assert.Contains("Get-Date -Format o", script);
    }

    [Fact]
    public void BuildScript_BodyUsingFunctionsAndReturn_IsNotTruncated()
    {
        // Les commandes de diagnostic définissent des fonctions locales et
        // utilisent « return » : elles doivent rester intactes.
        var body = "function Test-Port($t, $p) {\n    try { return $true } catch { return $false }\n}\n"
            + "$a = Test-Port 'dc1' 5985\n[PSCustomObject]@{ P5985 = $a }";

        var script = new SimplePowerShellCommand("Test", body).BuildScript();

        Assert.Contains("function Test-Port", script);
        Assert.Contains("return $true", script);
        Assert.Contains("[PSCustomObject]@{ P5985 = $a }", script);
    }
}