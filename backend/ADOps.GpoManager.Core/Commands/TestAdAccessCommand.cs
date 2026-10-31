namespace ADOps.GpoManager.Core.Commands;

/// <summary>Vérifie l'accès en lecture à AD DS via Get-ADDomain (module ActiveDirectory).</summary>
public sealed class TestAdAccessCommand : PowerShellCommand
{
    public override string Name => "AdAccess";
    public override TimeSpan Timeout { get; } = TimeSpan.FromSeconds(60);

    public override string ScriptBody =>
        """
        Import-Module ActiveDirectory -ErrorAction Stop
        Get-ADDomain -ErrorAction Stop | Select-Object Name, NetBIOSName, DistinguishedName, DomainControllersContainer
        """;
}
