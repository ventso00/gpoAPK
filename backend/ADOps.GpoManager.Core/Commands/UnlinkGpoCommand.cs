using System.Globalization;
using ADOps.GpoManager.Core.Validation;

namespace ADOps.GpoManager.Core.Commands;

/// <summary>
/// Supprime un lien de GPO via Remove-GPLink, puis relit les liens
/// de la cible (Get-GPInheritance) pour vérification.
/// </summary>
public sealed class UnlinkGpoCommand : PowerShellCommand
{
    public Guid GpoId { get; }
    public string TargetDistinguishedName { get; }

    public override string Name => "UnlinkGpo";
    public override TimeSpan Timeout { get; } = TimeSpan.FromMinutes(2);

    public UnlinkGpoCommand(Guid gpoId, string targetDistinguishedName)
    {
        if (gpoId == Guid.Empty)
        {
            throw new ArgumentException("L'identifiant (GUID) de la GPO est requis.", nameof(gpoId));
        }

        var (isValid, error) = DistinguishedNameValidator.ValidateDistinguishedName(targetDistinguishedName);
        if (!isValid)
        {
            throw new ArgumentException(error, nameof(targetDistinguishedName));
        }

        GpoId = gpoId;
        TargetDistinguishedName = targetDistinguishedName.Trim();
    }

    public override string ScriptBody
    {
        get
        {
            var target = DistinguishedNameValidator.EscapePowerShellLiteral(TargetDistinguishedName);

            return
                "Import-Module GroupPolicy -ErrorAction Stop\n" +
                "Import-Module ActiveDirectory -ErrorAction Stop\n" +
                "Remove-GPLink -Guid '" + GpoId.ToString() + "' -Target '" + target + "' -ErrorAction Stop\n" +
                "$domainDn = (Get-ADDomain -ErrorAction Stop).DistinguishedName\n" +
                "$links = Get-GPInheritance -Target '" + target + "' -ErrorAction Stop | Select-Object -ExpandProperty GpoLinks\n" +
                "$result = @{ DomainLinks = @(); OuLinks = @() }\n" +
                "$result.OuLinks = @($links | ForEach-Object { [PSCustomObject]@{ GpoId = $_.GpoId; Target = '" + target + "'; TargetType = 'OU'; Order = $_.Order; Enabled = $_.Enabled } })\n" +
                "$result";
        }
    }
}
