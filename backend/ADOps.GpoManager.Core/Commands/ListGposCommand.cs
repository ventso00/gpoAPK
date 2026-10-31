namespace ADOps.GpoManager.Core.Commands;

/// <summary>
/// Liste toutes les GPO du domaine via <c>Get-GPO -All</c> (cmdlet du module GroupPolicy).
/// Seules les propriétés exploitables sont projetées ; le tri est effectué côté PowerShell.
/// Une sortie vide (domaine sans GPO) n'est pas une erreur : elle produit une liste vide.
/// </summary>
public sealed class ListGposCommand : PowerShellCommand
{
    public override string Name => "ListGpos";
    public override TimeSpan Timeout { get; } = TimeSpan.FromMinutes(3);

    public override string ScriptBody =>
        """
        Import-Module GroupPolicy -ErrorAction Stop
        Get-GPO -All -ErrorAction Stop |
            Select-Object Id, DisplayName, Description, CreationTime, ModificationTime, GpoStatus, DomainName |
            Sort-Object DisplayName
        """;
}
