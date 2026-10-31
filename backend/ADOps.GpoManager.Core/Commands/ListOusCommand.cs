namespace ADOps.GpoManager.Core.Commands;

/// <summary>Liste les unités d'organisation du domaine via Get-ADOrganizationalUnit (module ActiveDirectory, RSAT).</summary>
public sealed class ListOusCommand : PowerShellCommand
{
    public override string Name => "ListOus";
    public override TimeSpan Timeout { get; } = TimeSpan.FromMinutes(2);

    public override string ScriptBody =>
        """
        Import-Module ActiveDirectory -ErrorAction Stop
        Get-ADOrganizationalUnit -Filter * -ErrorAction Stop |
            Select-Object Name, DistinguishedName, CanonicalName |
            Sort-Object CanonicalName
        """;
}
