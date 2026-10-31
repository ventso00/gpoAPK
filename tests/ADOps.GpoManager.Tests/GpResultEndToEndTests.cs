using System.Diagnostics;
using ADOps.GpoManager.Core.Commands;
using ADOps.GpoManager.PowerShell;
using Xunit;

namespace ADOps.GpoManager.Tests;

/// <summary>
/// Bout en bout : le script réellement envoyé au poste est exécuté par powershell.exe
/// sur la machine de test, puis son output est analysé par l'application. C'est la seule
/// manière de prouver que « Vérifier (gpresult) » renvoie bien la liste des GPO appliquées
/// plutôt qu'un code de sortie 1 ou une liste vide.
/// </summary>
public class GpResultEndToEndTests
{
    [Fact]
    public void GpResultScript_OnThisMachine_ReturnsParsableAppliedList()
    {
        var gpResult = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "gpresult.exe");
        if (!File.Exists(gpResult))
        {
            // Machine sans outil gpresult (RSAT absent ou Windows Home) : rien à vérifier ici.
            return;
        }

        var script = new GetGpResultCommand(Environment.MachineName).BuildScript();
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));

        var info = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };

        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(180000);

        Assert.True(process.ExitCode == 0, "gpresult a echoue : " + stderr);
        Assert.DoesNotContain("#< CLIXML", stderr, StringComparison.Ordinal);

        var names = GpResultReportParser.FromCommandOutput(stdout);
        var rawReport = ExtractRawOutput(stdout);

        // Le rapport doit être lisible et exploitable : la liste est vide seulement si
        // le poste n'a réellement aucune stratégie appliquée (cas d'un groupe de travail).
        Assert.False(string.IsNullOrWhiteSpace(rawReport), "Rapport gpresult vide.");
        Assert.DoesNotContain("Logging Mode", string.Join(" ", names), StringComparison.OrdinalIgnoreCase);

        // Un rapport qui présente une rubrique « appliquées » avec des entrées doit
        // produire une liste : c'est exactement le défaut qui vidait l'écran du poste.
        var declaresAppliedSection = rawReport!.IndexOf("Applied Group Policy Objects", StringComparison.OrdinalIgnoreCase) >= 0
            || rawReport.IndexOf("Stratégies de groupe appliquées", StringComparison.OrdinalIgnoreCase) >= 0;
        if (declaresAppliedSection)
        {
            Assert.NotEmpty(names);
        }

        foreach (var name in names)
        {
            Assert.True(rawReport!.Contains(name, StringComparison.Ordinal),
                "Le nom « " + name + " » ne provient pas du rapport gpresult.");
            Assert.DoesNotContain("Filtering", name, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string? ExtractRawOutput(string stdout)
    {
        var json = PowerShellOutputParser.ExtractJson(stdout);
        if (json.Length == 0 || json[0] != '{')
        {
            return null;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("RawOutput", out var value) ? value.GetString() : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
