using ADOps.GpoManager.Core.Validation;

namespace ADOps.GpoManager.Core.Commands;

/// <summary>
/// Déplace un compte utilisateur vers une autre unité d'organisation
/// (<c>Move-ADObject</c> avec <c>Get-ADUser</c>). Ce déplacement change les
/// GPO héritées par l'utilisateur : le lien de stratégie reste porté par l'OU.
/// </summary>
public sealed class MoveAdUserCommand : PowerShellCommand
{
    public string DistinguishedName { get; }
    public string TargetOuDistinguishedName { get; }

    public override string Name => "MoveAdUser";
    public override TimeSpan Timeout { get; } = TimeSpan.FromMinutes(2);

    public MoveAdUserCommand(string distinguishedName, string targetOuDistinguishedName)
    {
        var (dnValid, dnError) = DistinguishedNameValidator.ValidateDistinguishedName(
            distinguishedName, "le DistinguishedName de l'utilisateur");
        if (!dnValid)
        {
            throw new ArgumentException(dnError, nameof(distinguishedName));
        }

        var (ouValid, ouError) = DistinguishedNameValidator.ValidateDistinguishedName(
            targetOuDistinguishedName, "le DistinguishedName de l'unité d'organisation cible");
        if (!ouValid)
        {
            throw new ArgumentException(ouError, nameof(targetOuDistinguishedName));
        }

        if (!targetOuDistinguishedName.TrimStart().StartsWith("OU=", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "La cible doit être une unité d'organisation (OU=…), pas un conteneur d'objets.",
                nameof(targetOuDistinguishedName));
        }

        DistinguishedName = distinguishedName.Trim();
        TargetOuDistinguishedName = targetOuDistinguishedName.Trim();
    }

    public override string ScriptBody
    {
        get
        {
            var dn = DistinguishedNameValidator.EscapePowerShellLiteral(DistinguishedName);
            var target = DistinguishedNameValidator.EscapePowerShellLiteral(TargetOuDistinguishedName);
            return
                "$ErrorActionPreference = 'Stop'\n" +
                "Import-Module ActiveDirectory -ErrorAction Stop\n" +
                "$user = Get-ADUser -Identity '" + dn + "' -ErrorAction Stop\n" +
                "$ou = Get-ADOrganizationalUnit -Identity '" + target + "' -ErrorAction Stop\n" +
                "if ($user.DistinguishedName -eq ('CN=' + $user.SamAccountName + ',' + $ou.DistinguishedName)) { throw \"L'utilisateur est déjà dans cette unité d'organisation.\" }\n" +
                "$previous = $user.DistinguishedName\n" +
                "Move-ADObject -Identity $user.DistinguishedName -TargetPath $ou.DistinguishedName -ErrorAction Stop\n" +
                "[PSCustomObject]@{\n" +
                "    Name              = $user.Name\n" +
                "    SamAccountName    = $user.SamAccountName\n" +
                "    PreviousDn        = $previous\n" +
                "    TargetOu          = $ou.DistinguishedName\n" +
                "    TargetOuName      = $ou.Name\n" +
                "    Timestamp         = (Get-Date).ToUniversalTime().ToString('o')\n" +
                "}";
        }
    }

    private static string EscapeLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}