namespace ADOps.GpoManager.Core.Commands;

/// <summary>
/// Lit les liens de GPO du domaine et de chaque OU via Get-GPInheritance.
/// Les liens directs sont collectés ; l'héritage (InheritedGpoLinks) n'est pas inclus ici.
/// </summary>
public sealed class ListGpoLinksCommand : PowerShellCommand
{
    public override string Name => "ListGpoLinks";
    public override TimeSpan Timeout { get; } = TimeSpan.FromMinutes(3);

    public override string ScriptBody =>
        """
        Import-Module GroupPolicy -ErrorAction Stop
        Import-Module ActiveDirectory -ErrorAction Stop
        $domainDn = (Get-ADDomain -ErrorAction Stop).DistinguishedName
        $result = @{ DomainLinks = @(); OuLinks = @() }
        $domainLinks = Get-GPInheritance -Target $domainDn -ErrorAction Stop |
            Select-Object -ExpandProperty GpoLinks |
            ForEach-Object { [PSCustomObject]@{ GpoId = $_.GpoId; Target = $domainDn; TargetType = 'Domain'; Order = $_.Order; Enabled = $_.Enabled } }
        $result.DomainLinks = @($domainLinks)
        $ous = Get-ADOrganizationalUnit -Filter * -ErrorAction Stop
        foreach ($ou in $ous) {
            $links = Get-GPInheritance -Target $ou.DistinguishedName -ErrorAction Stop |
                Select-Object -ExpandProperty GpoLinks |
                ForEach-Object { [PSCustomObject]@{ GpoId = $_.GpoId; Target = $ou.DistinguishedName; TargetType = 'OU'; Order = $_.Order; Enabled = $_.Enabled } }
            if ($links) { $result.OuLinks += @($links) }
        }
        $result
        """;
}
