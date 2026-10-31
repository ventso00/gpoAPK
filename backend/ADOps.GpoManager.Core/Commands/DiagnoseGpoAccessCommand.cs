using ADOps.GpoManager.Core.Validation;

namespace ADOps.GpoManager.Core.Commands;

/// <summary>
/// Diagnostic d'accès GroupPolicy :
/// - lecture du catalogue GPO du domaine (Get-GPO -All) ;
/// - accessibilité du partage SYSVOL (\\&lt;domaine&gt;\SYSVOL) ;
/// - DN du domaine (cible des liens au niveau domaine).
/// </summary>
public sealed class DiagnoseGpoAccessCommand : PowerShellCommand
{
    public string DomainFqdn { get; }

    public override string Name => "DiagnoseGpoAccess";
    public override TimeSpan Timeout { get; } = TimeSpan.FromMinutes(2);

    public DiagnoseGpoAccessCommand(string domainFqdn)
    {
        var (isValid, error) = DomainValidator.ValidateDomainName(domainFqdn);
        if (!isValid)
        {
            throw new ArgumentException(error, nameof(domainFqdn));
        }

        DomainFqdn = DomainValidator.NormalizeFqdn(domainFqdn);
    }

    public override string ScriptBody
    {
        get
        {
            var domain = DistinguishedNameValidator.EscapePowerShellLiteral(DomainFqdn);

            return
                "$domain = '" + domain + "'\n" +
                "Import-Module GroupPolicy -ErrorAction Stop\n" +
                "Import-Module ActiveDirectory -ErrorAction Stop\n" +
                "$gpos = @(Get-GPO -All -ErrorAction Stop)\n" +
                "$domainDn = (Get-ADDomain -Server $domain -ErrorAction SilentlyContinue).DistinguishedName\n" +
                "$sysvolReachable = $false\n" +
                "try { $sysvolReachable = Test-Path ('\\\\' + $domain + '\\SYSVOL') } catch {}\n" +
                "@{\n" +
                "    GpoCount        = $gpos.Count\n" +
                "    DomainDn        = $domainDn\n" +
                "    SysvolReachable = $sysvolReachable\n" +
                "    Success         = ($gpos.Count -ge 0 -and $sysvolReachable)\n" +
                "}";
        }
    }
}
