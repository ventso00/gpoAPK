namespace ADOps.GpoManager.Core.Commands;

/// <summary>Vérifie que le module GroupPolicy fonctionne via Get-GPO -All et retourne le nombre de GPO.</summary>
public sealed class TestGroupPolicyCapabilityCommand : PowerShellCommand
{
    public override string Name => "GroupPolicyCapability";
    public override TimeSpan Timeout { get; } = TimeSpan.FromMinutes(2);

    public override string ScriptBody =>
        """
        Import-Module GroupPolicy -ErrorAction Stop
        $gpos = Get-GPO -All -ErrorAction Stop
        [PSCustomObject]@{ Count = @($gpos).Count }
        """;
}
