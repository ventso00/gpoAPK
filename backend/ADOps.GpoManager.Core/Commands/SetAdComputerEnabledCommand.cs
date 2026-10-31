using ADOps.GpoManager.Core.Validation;

namespace ADOps.GpoManager.Core.Commands;

/// <summary>
/// Active ou désactive le compte d'ordinateur dans AD DS
/// (<c>Enable-ADAccount</c> / <c>Disable-ADAccount</c>).
/// Le compte désactivé ne peut plus s'authentifier : la machine ne reçoit
/// plus ses stratégies tant qu'il n'est pas réactivé.
/// </summary>
public sealed class SetAdComputerEnabledCommand : PowerShellCommand
{
    public string DistinguishedName { get; }
    public bool Enabled { get; }

    public override string Name => "SetAdComputerEnabled";

    public SetAdComputerEnabledCommand(string distinguishedName, bool enabled)
    {
        var (isValid, error) = DistinguishedNameValidator.ValidateDistinguishedName(
            distinguishedName, "le DistinguishedName de l'ordinateur");
        if (!isValid)
        {
            throw new ArgumentException(error, nameof(distinguishedName));
        }

        DistinguishedName = distinguishedName.Trim();
        Enabled = enabled;
    }

    public override string ScriptBody
    {
        get
        {
            var dn = DistinguishedNameValidator.EscapePowerShellLiteral(DistinguishedName);
            var action = Enabled ? "Enable-ADAccount" : "Disable-ADAccount";
            return
                "$ErrorActionPreference = 'Stop'\n" +
                "Import-Module ActiveDirectory -ErrorAction Stop\n" +
                "$computer = Get-ADComputer -Identity '" + dn + "' -ErrorAction Stop\n" +
                action + " -Identity $computer.DistinguishedName -ErrorAction Stop\n" +
                "$updated = Get-ADComputer -Identity $computer.DistinguishedName -Properties Enabled -ErrorAction Stop\n" +
                "[PSCustomObject]@{\n" +
                "    Name              = $updated.Name\n" +
                "    SamAccountName    = $updated.SamAccountName\n" +
                "    DistinguishedName = $updated.DistinguishedName\n" +
                "    Enabled           = [bool]$updated.Enabled\n" +
                "    Action            = '" + (Enabled ? "Enabled" : "Disabled") + "'\n" +
                "    Timestamp         = (Get-Date).ToUniversalTime().ToString('o')\n" +
                "}";
        }
    }
}
