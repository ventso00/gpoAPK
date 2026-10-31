using ADOps.GpoManager.Core.Validation;

namespace ADOps.GpoManager.Core.Commands;

/// <summary>
/// Déplace un compte d'ordinateur vers une autre unité d'organisation
/// (<c>Move-ADObject</c>). C'est ce déplacement qui change les GPO héritées
/// par le poste : le lien de stratégie reste porté par l'OU.
/// </summary>
public sealed class MoveAdComputerCommand : PowerShellCommand
{
    public string DistinguishedName { get; }
    public string TargetOuDistinguishedName { get; }

    public override string Name => "MoveAdComputer";

    public MoveAdComputerCommand(string distinguishedName, string targetOuDistinguishedName)
    {
        var (dnValid, dnError) = DistinguishedNameValidator.ValidateDistinguishedName(
            distinguishedName, "le DistinguishedName de l'ordinateur");
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
                "$computer = Get-ADComputer -Identity '" + dn + "' -ErrorAction Stop\n" +
                "$ou = Get-ADOrganizationalUnit -Identity '" + target + "' -ErrorAction Stop\n" +
                "if ($computer.DistinguishedName -eq ('CN=' + $computer.Name + ',' + $ou.DistinguishedName)) { throw \"L'ordinateur est déjà dans cette unité d'organisation.\" }\n" +
                "$previous = $computer.DistinguishedName\n" +
                "Move-ADObject -Identity $computer.DistinguishedName -TargetPath $ou.DistinguishedName -ErrorAction Stop\n" +
                "[PSCustomObject]@{\n" +
                "    Name              = $computer.Name\n" +
                "    SamAccountName    = $computer.SamAccountName\n" +
                "    PreviousDn        = $previous\n" +
                "    TargetOu          = $ou.DistinguishedName\n" +
                "    TargetOuName      = $ou.Name\n" +
                "    Timestamp         = (Get-Date).ToUniversalTime().ToString('o')\n" +
                "}";
        }
    }
}
