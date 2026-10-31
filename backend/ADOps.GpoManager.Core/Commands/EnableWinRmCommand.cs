namespace ADOps.GpoManager.Core.Commands;

/// <summary>
/// Remédiation WinRM — TOUJOURS exécutée en local (principe « fail-safe » :
/// un WinRM hors ligne ne peut pas être réparé à travers WinRM).
/// Active PSRemoting, le démarrage automatique du service, et les règles de
/// pare-feu du groupe « Windows Remote Management ».
/// Exige des droits administrateur (élévation) — uniquement sur confirmation
/// explicite de l'utilisateur.
/// Aucune configuration non sécurisée n'est ajoutée (pas de TrustedHosts,
/// pas de HTTP sans chiffrement imposé) : les choix risqués restent manuels.
/// </summary>
public sealed class EnableWinRmCommand : PowerShellCommand
{
    public override string Name => "EnableWinRm";
    public override TimeSpan Timeout { get; } = TimeSpan.FromMinutes(3);

    public override string ScriptBody =>
        """
        $isAdmin = $false
        try {
            $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
            $principal = New-Object Security.Principal.WindowsPrincipal($identity)
            $isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
        } catch {}
        if (-not $isAdmin) {
            @{ Applied = $false; Message = 'Droits administrateur requis : relancez ADOps GPO Manager en tant qu''administrateur.' }
            return
        }
        $steps = @()
        try { Enable-PSRemoting -Force -ErrorAction Stop | Out-Null; $steps += 'PSRemoting active' } catch { $steps += ('Enable-PSRemoting : ' + $_.Exception.Message) }
        try { Set-Service -Name WinRM -StartupType Automatic -ErrorAction Stop; $steps += 'Service WinRM en demarrage automatique' } catch { $steps += ('Set-Service : ' + $_.Exception.Message) }
        try { Start-Service -Name WinRM -ErrorAction SilentlyContinue; $steps += 'Service WinRM demarre' } catch { $steps += ('Start-Service : ' + $_.Exception.Message) }
        try { Get-NetFirewallRule -Group 'Windows Remote Management' -ErrorAction SilentlyContinue | Enable-NetFirewallRule -ErrorAction SilentlyContinue; $steps += 'Regles de pare-feu WinRM activees' } catch { $steps += ('Pare-feu : ' + $_.Exception.Message) }
        $serviceState = 'Inconnu'
        try { $serviceState = (Get-Service -Name WinRM -ErrorAction SilentlyContinue).Status.ToString() } catch {}
        @{ Applied = ($serviceState -eq 'Running'); ServiceState = $serviceState; Steps = @($steps) }
        """;
}
