using System.Text.RegularExpressions;
using ADOps.GpoManager.Core.Validation;

namespace ADOps.GpoManager.Core.Commands;

/// <summary>
/// Retire un ordinateur (ou un compte) du groupe de filtrage d'une GPO.
/// L'objet reste dans son OU, mais la stratégie ne lui est plus appliquée
/// au prochain traitement ; le lien de la GPO n'est pas supprimé.
/// </summary>
public sealed class RemoveGpoTargetMemberCommand : PowerShellCommand
{
    private static readonly Regex GroupNamePattern = new("^[A-Za-z0-9_-]{1,20}$", RegexOptions.Compiled);

    public string GroupName { get; }

    public string MemberDistinguishedName { get; }

    public override string Name => "RemoveGpoTargetMember";
    public override TimeSpan Timeout { get; } = TimeSpan.FromMinutes(2);

    public RemoveGpoTargetMemberCommand(string groupName, string memberDistinguishedName)
    {
        if (string.IsNullOrWhiteSpace(groupName) || !GroupNamePattern.IsMatch(groupName))
        {
            throw new ArgumentException("Nom de groupe invalide.", nameof(groupName));
        }

        var (isValid, error) = DistinguishedNameValidator.ValidateDistinguishedName(memberDistinguishedName);
        if (!isValid)
        {
            throw new ArgumentException(error, nameof(memberDistinguishedName));
        }

        GroupName = groupName;
        MemberDistinguishedName = memberDistinguishedName.Trim();
    }

    public override string ScriptBody
    {
        get
        {
            var group = Escape(GroupName);
            var member = Escape(MemberDistinguishedName);

            return
                "Import-Module ActiveDirectory -ErrorAction Stop\n" +
                "$group = Get-ADGroup -Identity '" + group + "' -ErrorAction Stop\n" +
                "$dn = '" + member + "'\n" +
                "$isMember = $false\n" +
                "try { $isMember = @(Get-ADGroupMember -Identity $group -ErrorAction Stop | Where-Object { $_.DistinguishedName -eq $dn }).Count -gt 0 } catch { $isMember = $false }\n" +
                "if ($isMember) { Remove-ADGroupMember -Identity $group -Members $dn -Confirm:$false -ErrorAction Stop }\n" +
                "$remaining = @(Get-ADGroupMember -Identity $group -ErrorAction SilentlyContinue).Count\n" +
                "[PSCustomObject]@{ GroupName = $group.Name; Removed = $isMember; RemainingMembers = $remaining }";
        }
    }

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
