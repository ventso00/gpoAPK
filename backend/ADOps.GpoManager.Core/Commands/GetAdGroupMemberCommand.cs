using ADOps.GpoManager.Core.Validation;

namespace ADOps.GpoManager.Core.Commands;

/// <summary>
/// Liste les membres d'un groupe de sécurité AD DS.
/// Utilisé pour vérifier les membres du groupe de filtrage d'une GPO.
/// </summary>
public sealed class GetAdGroupMemberCommand : PowerShellCommand
{
    public string GroupDistinguishedName { get; }

    public GetAdGroupMemberCommand(string groupDistinguishedName)
    {
        var (isValid, error) = DistinguishedNameValidator.ValidateDistinguishedName(groupDistinguishedName);
        if (!isValid)
        {
            throw new ArgumentException(error, nameof(groupDistinguishedName));
        }

        GroupDistinguishedName = groupDistinguishedName.Trim();
    }

    public override string Name => "GetAdGroupMember";
    public override TimeSpan Timeout { get; } = TimeSpan.FromMinutes(2);

    public override string ScriptBody
    {
        get
        {
            var group = EscapeLiteral(GroupDistinguishedName);

            return
                "Import-Module ActiveDirectory -ErrorAction Stop\n" +
                "$members = Get-ADGroupMember -Identity '" + group + "' -ErrorAction Stop | " +
                "Select-Object SamAccountName, Name, DistinguishedName, objectClass | " +
                "Sort-Object SamAccountName | " +
                "ConvertTo-Json -Depth 3 -Compress";
        }
    }

    private static string EscapeLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}