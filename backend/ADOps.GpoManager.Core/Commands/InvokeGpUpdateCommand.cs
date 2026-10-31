namespace ADOps.GpoManager.Core.Commands;

/// <summary>
/// Force l'application des stratégies sur un poste client :
/// <c>gpupdate /force</c>.
///
/// <para>
/// La commande doit être exécutée **sur le poste cible** : elle est donc
/// destinée à l'exécuteur distant (WinRM). Sur un poste où WinRM n'est pas
/// activé, l'opération est signalée comme non réalisée — jamais comme
/// réussie.
/// </para>
/// </summary>
public sealed class InvokeGpUpdateCommand : PowerShellCommand
{
    /// <summary>Nom de la machine cible (pour le journal et l'audit).</summary>
    public string TargetComputer { get; }

    /// <summary>Portée du rafraîchissement (« computer » ou « user »).</summary>
    public string Scope { get; }

    public override string Name => "InvokeGpUpdate";
    public override TimeSpan Timeout { get; } = TimeSpan.FromMinutes(3);

    public InvokeGpUpdateCommand(string targetComputer, string scope = "computer")
    {
        if (string.IsNullOrWhiteSpace(targetComputer))
        {
            throw new ArgumentException("Le nom de la machine cible est requis.", nameof(targetComputer));
        }

        TargetComputer = targetComputer.Trim();
        Scope = string.Equals(scope, "user", StringComparison.OrdinalIgnoreCase) ? "user" : "computer";
    }

    public override string ScriptBody
    {
        get
        {
            // gpupdate.exe peut écrire sur stderr (avertissements de redémarrage
            // différé). La redirection 2>&1 sous $ErrorActionPreference='Stop'
            // fabrique une NativeCommandError relayée en CLIXML par WinRM : le
            // canal fichiers de Start-Process rend le texte brut sans erreur.
            return
                "$ErrorActionPreference = 'Stop'\n" +
                "$stdout = Join-Path ([System.IO.Path]::GetTempPath()) ('adops-gpupdate-' + [Guid]::NewGuid().ToString('N') + '.out')\n" +
                "$stderr = Join-Path ([System.IO.Path]::GetTempPath()) ('adops-gpupdate-' + [Guid]::NewGuid().ToString('N') + '.err')\n" +
                "try {\n" +
                "    $proc = Start-Process -FilePath gpupdate.exe -ArgumentList '/force', '/target:" + Scope + "' -NoNewWindow -Wait -PassThru `\n" +
                "        -RedirectStandardOutput $stdout -RedirectStandardError $stderr\n" +
                "    $exit = if ($null -ne $proc) { $proc.ExitCode } else { -1 }\n" +
                "    $outText = ''\n" +
                "    if (Test-Path -LiteralPath $stdout) { $outText += Get-Content -LiteralPath $stdout -Raw }\n" +
                "    if (Test-Path -LiteralPath $stderr) { $outText += Get-Content -LiteralPath $stderr -Raw }\n" +
                "    if ($exit -ne 0) {\n" +
                "        throw ('gpupdate a échoué sur ce poste (code de sortie ' + $exit + ') : ' + $outText.Trim())\n" +
                "    }\n" +
                "    [PSCustomObject]@{\n" +
                "        Computer  = $env:COMPUTERNAME\n" +
                "        ExitCode  = $exit\n" +
                "        Scope     = '" + Scope + "'\n" +
                "        Output    = $outText.Trim()\n" +
                "        Timestamp = (Get-Date).ToUniversalTime().ToString('o')\n" +
                "    }\n" +
                "} finally {\n" +
                "    foreach ($f in @($stdout, $stderr)) {\n" +
                "        if ($null -ne $f -and (Test-Path -LiteralPath $f)) { Remove-Item -LiteralPath $f -Force -ErrorAction SilentlyContinue }\n" +
                "    }\n" +
                "}";
        }
    }
}
