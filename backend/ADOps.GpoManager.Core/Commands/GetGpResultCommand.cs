namespace ADOps.GpoManager.Core.Commands;

/// <summary>
/// Relaît le résultat effectif des stratégies appliquées sur un poste client :
/// <c>gpresult /r</c> (liste des GPO appliquées).
///
/// <para>
/// La commande s'exécute **sur le poste cible** (exécuteur distant WinRM).
/// C'est la seule preuve directe qu'une GPO est réellement appliquée : le
/// cache local ne contient qu'une intention de déploiement.
/// </para>
/// </summary>
public sealed class GetGpResultCommand : PowerShellCommand
{
    /// <summary>Nom de la machine cible (pour le journal et l'audit).</summary>
    public string TargetComputer { get; }

    public override string Name => "GetGpResult";
    public override TimeSpan Timeout { get; } = TimeSpan.FromMinutes(3);

    public GetGpResultCommand(string targetComputer)
    {
        if (string.IsNullOrWhiteSpace(targetComputer))
        {
            throw new ArgumentException("Le nom de la machine cible est requis.", nameof(targetComputer));
        }

        TargetComputer = targetComputer.Trim();
    }

    public override string ScriptBody
    {
        get
        {
            // gpresult.exe écrit des avertissements sur stderr MÊME en succès.
            // Rediriger ce flux via 2>&1 sous $ErrorActionPreference='Stop' (sémantique
            // Windows PowerShell 5.1) fabrique une NativeCommandError : le canal WinRM
            // la renvoie sérialisée (#< CLIXML) et powershell.exe sort en code 1 alors
            // que le rapport est valide. Start-Process avec redirections fichiers écrit
            // les octets bruts sans jamais créer d'enregistrement d'erreur, et seule la
            // valeur de sortie de gpresult décide du succès.
            //
            // Le script ne devine aucune liste de GPO : il fournit le rapport brut. La
            // sélection des GPO réellement appliquées est faite côté application
            // (GpResultReportParser), où la lecture par sections est testée ; le même
            // analyseur traite le repli XML, ce qui évite deux implémentations divergentes.
            return
                "$ErrorActionPreference = 'Stop'\n" +
                "$tmp = [System.IO.Path]::GetTempPath()\n" +
                "$stdout = Join-Path $tmp ('adops-gpresult-out-' + [Guid]::NewGuid().ToString('N') + '.txt')\n" +
                "$stderr = Join-Path $tmp ('adops-gpresult-err-' + [Guid]::NewGuid().ToString('N') + '.txt')\n" +
                "$xmlPath = Join-Path $tmp ('adops-gpresult-report-' + [Guid]::NewGuid().ToString('N') + '.xml')\n" +
                "try {\n" +
                "    $proc = Start-Process -FilePath gpresult.exe -ArgumentList '/r' -NoNewWindow -Wait -PassThru `\n" +
                "        -RedirectStandardOutput $stdout -RedirectStandardError $stderr\n" +
                "    $exit = if ($null -ne $proc) { $proc.ExitCode } else { -1 }\n" +
                "    $raw = ''\n" +
                "    if (Test-Path -LiteralPath $stdout) {\n" +
                "        $bytes = [System.IO.File]::ReadAllBytes($stdout)\n" +
                "        # gpresult peut ecrire de l'UTF-8 (sortie redirectee) ou de la page\n" +
                "        # de codes OEM de la console : UTF-8 strict d'abord, il echoue sur les\n" +
                "        # octets OEM, donc la page OEM sert de repli plutot que de deviner.\n" +
                "        try {\n" +
                "            $raw = (New-Object System.Text.UTF8Encoding($false, $true)).GetString($bytes)\n" +
                "        } catch {\n" +
                "            $cp = 437\n" +
                "            try {\n" +
                "                $cp = [int](Get-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Nls\\CodePage' -Name OEMCP -ErrorAction Stop).OEMCP\n" +
                "            } catch { }\n" +
                "            $raw = [System.Text.Encoding]::GetEncoding($cp).GetString($bytes)\n" +
                "        }\n" +
                "    }\n" +
                "    if ($exit -ne 0) {\n" +
                "        $errText = ''\n" +
                "        if (Test-Path -LiteralPath $stderr) { $errText = Get-Content -LiteralPath $stderr -Raw }\n" +
                "        if ([string]::IsNullOrWhiteSpace($errText)) { $errText = 'gpresult a renvoye un rapport vide.' }\n" +
                "        throw ('gpresult a echoue sur ce poste (code de sortie ' + $exit + ') : ' + $errText.Trim())\n" +
                "    }\n" +
                "    $xml = ''\n" +
                "    if ([string]::IsNullOrWhiteSpace($raw)) {\n" +
                "        # Repli independant de la console : le rapport XML est en UTF-16 avec\n" +
                "        # BOM, lu par .NET qui detecte l'encodage, et sa structure ne depend\n" +
                "        # pas de la langue de l'interface.\n" +
                "        $procXml = Start-Process -FilePath gpresult.exe -ArgumentList '/r', '/x', ('\"' + $xmlPath + '\"') `\n" +
                "            -NoNewWindow -Wait -PassThru -RedirectStandardError $stderr\n" +
                "        $exit = if ($null -ne $procXml) { $procXml.ExitCode } else { $exit }\n" +
                "        if (Test-Path -LiteralPath $xmlPath) { $xml = [System.IO.File]::ReadAllText($xmlPath) }\n" +
                "    }\n" +
                "    if ($exit -ne 0 -and [string]::IsNullOrWhiteSpace($raw) -and [string]::IsNullOrWhiteSpace($xml)) {\n" +
                "        throw ('gpresult n a produit aucun rapport sur ce poste (code de sortie ' + $exit + ').')\n" +
                "    }\n" +
                "    [PSCustomObject]@{\n" +
                "        Computer  = $env:COMPUTERNAME\n" +
                "        ExitCode  = $exit\n" +
                "        RawOutput = $raw\n" +
                "        XmlReport = $xml\n" +
                "        Timestamp = (Get-Date).ToUniversalTime().ToString('o')\n" +
                "    }\n" +
                "} finally {\n" +
                "    foreach ($f in @($stdout, $stderr, $xmlPath)) {\n" +
                "        if ($null -ne $f -and (Test-Path -LiteralPath $f)) { Remove-Item -LiteralPath $f -Force -ErrorAction SilentlyContinue }\n" +
                "    }\n" +
                "}";
        }
    }
}
