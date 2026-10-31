namespace ADOps.GpoManager.Core.Commands;

/// <summary>
/// Remédiation RSAT — installation des fonctionnalités Windows
/// (Rsat.ActiveDirectory.DS-LDS.Tools et Rsat.GroupPolicy.Management.Tools).
/// Exécution locale uniquement, droits administrateur obligatoires,
/// uniquement sur confirmation explicite.
/// </summary>
public sealed class InstallRsatCommand : PowerShellCommand
{
    public override string Name => "InstallRsat";
    public override TimeSpan Timeout { get; } = TimeSpan.FromMinutes(10);

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
        $capabilities = @(
            'Rsat.ActiveDirectory.DS-LDS.Tools~~~~0.0.1.0',
            'Rsat.GroupPolicy.Management.Tools~~~~0.0.1.0'
        )
        $results = @()
        $allOk = $true
        foreach ($capability in $capabilities) {
            try {
                $state = (Get-WindowsCapability -Online -Name $capability -ErrorAction Stop).State
                if ($state -ne 'Installed') {
                    Add-WindowsCapability -Online -Name $capability -ErrorAction Stop | Out-Null
                    $state = (Get-WindowsCapability -Online -Name $capability -ErrorAction Stop).State
                }
                $results += ($capability.Split('~')[0] + ' : ' + $state)
                if ($state -ne 'Installed') { $allOk = $false }
            } catch {
                $allOk = $false
                $results += ($capability.Split('~')[0] + ' : ' + $_.Exception.Message)
            }
        }
        @{ Applied = $allOk; Capabilities = @($results) }
        """;
}
