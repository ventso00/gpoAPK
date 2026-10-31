namespace ADOps.GpoManager.Core.Commands;

/// <summary>
/// Liste les objets ordinateurs du domaine via Get-ADComputer (module ActiveDirectory, RSAT).
/// Seuls les attributs exploités sont projetés ; LastLogonDate est converti en UTC ISO 8601.
/// </summary>
public sealed class ListComputersCommand : PowerShellCommand
{
    public override string Name => "ListComputers";
    public override TimeSpan Timeout { get; } = TimeSpan.FromMinutes(3);

    public override string ScriptBody =>
        """
        Import-Module ActiveDirectory -ErrorAction Stop
        Get-ADComputer -Filter * -ErrorAction Stop -Properties Name, DistinguishedName, SamAccountName, OperatingSystem, OperatingSystemVersion, Enabled, LastLogonDate, IPv4Address |
            Select-Object Name, DistinguishedName, SamAccountName, OperatingSystem, OperatingSystemVersion, Enabled, IPv4Address,
                @{Name='LastLogonUtc'; Expression={ if ($null -ne $_.LastLogonDate) { $_.LastLogonDate.ToUniversalTime().ToString('o') } else { $null } }} |
            Sort-Object Name
        """;
}
