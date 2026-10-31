using ADOps.GpoManager.Core.Validation;

namespace ADOps.GpoManager.Core.Commands;

/// <summary>
/// Obtient l'ensemble résultant des stratégies (RSoP) d'un ordinateur via gpresult.
/// Cette commande s'exécute SUR la machine cible (via WinRM) : gpresult tourne localement sur le poste,
/// l'authentification est celle du canal WinRM — aucun mot de passe n'apparaît dans une ligne de commande.
/// Le rapport XML temporaire est lu puis supprimé.
/// </summary>
public sealed class GetGpoResultantSetCommand : PowerShellCommand
{
    public string ComputerName { get; }

    public override string Name => "GetGpoResultantSet";
    public override TimeSpan Timeout { get; } = TimeSpan.FromMinutes(3);

    public GetGpoResultantSetCommand(string computerName)
    {
        var (isValid, error) = DomainValidator.ValidateServerFqdn(computerName);
        if (!isValid)
        {
            throw new ArgumentException(error, nameof(computerName));
        }

        ComputerName = DomainValidator.NormalizeFqdn(computerName);
    }

    public override string ScriptBody =>
        """
        $tempPath = Join-Path $env:TEMP ('adops-rsop-' + [Guid]::NewGuid().ToString('N') + '.xml')
        $stderrPath = Join-Path $env:TEMP ('adops-rsop-' + [Guid]::NewGuid().ToString('N') + '.err')
        try {
            # Start-Process + fichiers : le stderr natif de gpresult ne peut plus
            # devenir une NativeCommandError sérialisée (CLIXML) sous ErrorActionPreference Stop.
            $proc = Start-Process -FilePath gpresult.exe -ArgumentList '/SCOPE', 'COMPUTER', '/X', ('"' + $tempPath + '"') `
                -NoNewWindow -Wait -PassThru -RedirectStandardError $stderrPath
            $exit = if ($null -ne $proc) { $proc.ExitCode } else { -1 }
            if ($exit -ne 0) {
                $errText = ''
                if (Test-Path -LiteralPath $stderrPath) { $errText = Get-Content -LiteralPath $stderrPath -Raw }
                if ([string]::IsNullOrWhiteSpace($errText)) { $errText = 'gpresult na pas produit de rapport.' }
                throw ("gpresult a échoué sur cet ordinateur (code de sortie $exit) : " + $errText.Trim())
            }
            if (-not (Test-Path -LiteralPath $tempPath)) {
                throw 'gpresult na produit aucun rapport XML sur ce poste.'
            }
            # gpresult /x ecrit en UTF-16LE avec BOM : ReadAllText detecte l'encodage,
            # Get-Content -Encoding UTF8 produirait un texte nullifie illisible.
            $report = [System.IO.File]::ReadAllText($tempPath)
            $report
        } finally {
            foreach ($f in @($tempPath, $stderrPath)) {
                if ($null -ne $f -and (Test-Path -LiteralPath $f)) { Remove-Item -LiteralPath $f -Force -ErrorAction SilentlyContinue }
            }
        }
        """;
}
