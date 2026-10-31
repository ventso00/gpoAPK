using ADOps.GpoManager.Core.Validation;

namespace ADOps.GpoManager.Core.Commands;

/// <summary>
/// Retire un membre (ordinateur ou utilisateur) d'un groupe de sécurité AD DS.
/// Utilisé pour le détachement d'un poste d'une GPO via le groupe de filtrage.
/// </summary>
public sealed class RemoveAdGroupMemberCommand : PowerShellCommand
{
    public string GroupDistinguishedName { get; }
    public string MemberDistinguishedName { get; }

    public RemoveAdGroupMemberCommand(string groupDistinguishedName, string memberDistinguishedName)
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

    public override string Name => "RemoveAdGroupMember";
    public override TimeSpan Timeout { get; } = TimeSpan.FromMinutes(2);

    public override string ScriptBody
    {
        get
        {
            var group = EscapeLiteral(GroupDistinguishedName);
            var member = EscapeLiteral(MemberDistinguishedName);

            return
                "Import-Module ActiveDirectory -ErrorAction Stop\n" +
                "Remove-ADGroupMember -Identity '" + group + "' -Members '" + member + "' -Confirm:$false -ErrorAction Stop\n" +
                "[PSCustomObject]@{ Group = '" + group + "'; Member = '" + member + "'; Success = $true }";
        }
    }

    private static string EscapeLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}