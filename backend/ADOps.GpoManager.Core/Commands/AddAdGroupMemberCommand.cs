using ADOps.GpoManager.Core.Validation;

namespace ADOps.GpoManager.Core.Commands;

/// <summary>
/// Ajoute un membre (ordinateur ou utilisateur) à un groupe de sécurité AD DS.
/// Utilisé pour le ciblage de sécurité des GPO : le poste/utilisateur est ajouté
/// au groupe de filtrage de la GPO.
/// </summary>
public sealed class AddAdGroupMemberCommand : PowerShellCommand
{
    public string GroupDistinguishedName { get; }
    public string MemberDistinguishedName { get; }

    public AddAdGroupMemberCommand(string groupDistinguishedName, string memberDistinguishedName)
    {
        var (groupValid, groupError) = DistinguishedNameValidator.ValidateDistinguishedName(groupDistinguishedName);
        if (!groupValid)
        {
            throw new ArgumentException(groupError, nameof(groupDistinguishedName));
        }

        var (memberValid, memberError) = DistinguishedNameValidator.ValidateDistinguishedName(memberDistinguishedName);
        if (!memberValid)
        {
            throw new ArgumentException(memberError, nameof(memberDistinguishedName));
        }

        GroupDistinguishedName = groupDistinguishedName.Trim();
        MemberDistinguishedName = memberDistinguishedName.Trim();
    }

    public override string Name => "AddAdGroupMember";
    public override TimeSpan Timeout { get; } = TimeSpan.FromMinutes(2);

    public override string ScriptBody
    {
        get
        {
            var group = EscapeLiteral(GroupDistinguishedName);
            var member = EscapeLiteral(MemberDistinguishedName);
            
            return
                "Import-Module ActiveDirectory -ErrorAction Stop\n" +
                "Add-ADGroupMember -Identity '" + group + "' -Members '" + member + "' -ErrorAction Stop\n" +
                "[PSCustomObject]@{ Group = '" + group + "'; Member = '" + member + "'; Success = $true }";
        }
    }

    private static string EscapeLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}