namespace ADOps.GpoManager.Core.Commands;

/// <summary>
/// Diagnostic des modules RSAT locaux (ActiveDirectory / GroupPolicy),
/// de la version du système et des droits administrateur — informations
/// nécessaires pour guider (ou décider) l'installation des fonctionnalités.
/// </summary>
public sealed class DiagnoseRsatCommand : PowerShellCommand
{
    public override string Name => "DiagnoseRsat";
    public override TimeSpan Timeout { get; } = TimeSpan.FromSeconds(60);

    public override string ScriptBody =>
        """
        $modules = @(Get-Module -ListAvailable -Name GroupPolicy,ActiveDirectory -ErrorAction SilentlyContinue)
        $adModule = @($modules | Where-Object { $_.Name -eq 'ActiveDirectory' })
        $gpoModule = @($modules | Where-Object { $_.Name -eq 'GroupPolicy' })
        $os = Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue
        $isAdmin = $false
        try {
            $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
            $principal = New-Object Security.Principal.WindowsPrincipal($identity)
            $isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
        } catch {}
        @{
            ActiveDirectoryModule = ($adModule.Count -gt 0)
            ActiveDirectoryVersion = $(if ($adModule.Count -gt 0) { $adModule[0].Version.ToString() } else { $null })
            GroupPolicyModule     = ($gpoModule.Count -gt 0)
            GroupPolicyVersion    = $(if ($gpoModule.Count -gt 0) { $gpoModule[0].Version.ToString() } else { $null })
            OsCaption             = $(if ($null -ne $os) { $os.Caption } else { $null })
            OsVersion             = $(if ($null -ne $os) { $os.Version } else { $null })
            IsAdministrator       = $isAdmin
            Success               = ($adModule.Count -gt 0 -and $gpoModule.Count -gt 0)
        }
        """;
}
