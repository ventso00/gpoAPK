using System.Globalization;
using ADOps.GpoManager.Core.Validation;

namespace ADOps.GpoManager.Core.Commands;

/// <summary>
/// Lie une GPO à une cible (domaine ou OU) via New-GPLink, puis relit
/// les liens de la cible (Get-GPInheritance) pour vérification.
/// La commande est idempotent : lier une GPO déjà liée à la même cible
/// ne crée pas de doublon (AD DS met à jour le lien existant).
/// </summary>
public sealed class LinkGpoCommand : PowerShellCommand
{
    public Guid GpoId { get; }
    public string TargetDistinguishedName { get; }
    public int? Order { get; }
    public bool Enabled { get; }

    public override string Name => "LinkGpo";
    public override TimeSpan Timeout { get; } = TimeSpan.FromMinutes(2);

    public LinkGpoCommand(Guid gpoId, string targetDistinguishedName, int? order = null, bool enabled = true)
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

        if (order is < 0 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(order), "L'ordre du lien doit être compris entre 0 et 1000.");
        }

        GpoId = gpoId;
        TargetDistinguishedName = targetDistinguishedName.Trim();
        Order = order;
        Enabled = enabled;
    }

    public override string ScriptBody
    {
        get
        {
            // Le DN validé est échappé (guillemets simples doublés) avant injection.
            var target = DistinguishedNameValidator.EscapePowerShellLiteral(TargetDistinguishedName);
            var options = (Order.HasValue
                ? " -Order " + Order.Value.ToString(CultureInfo.InvariantCulture)
                : string.Empty) + (Enabled ? string.Empty : " -Disabled");

            return
                "Import-Module GroupPolicy -ErrorAction Stop\n" +
                "Import-Module ActiveDirectory -ErrorAction Stop\n" +
                "$existing = @((Get-GPInheritance -Target '" + target + "' -ErrorAction Stop).GpoLinks | Where-Object { $_.GpoId -eq '" + GpoId.ToString() + "' })\n" +
                "if ($existing.Count -eq 0) {\n" +
                "  New-GPLink -Guid '" + GpoId.ToString() + "' -Target '" + target + "'" + options + " -ErrorAction Stop | Out-Null\n" +
                "}\n" +
                "$links = Get-GPInheritance -Target '" + target + "' -ErrorAction Stop | Select-Object -ExpandProperty GpoLinks\n" +
                "$result = @{ DomainLinks = @(); OuLinks = @() }\n" +
                "$result.OuLinks = @($links | ForEach-Object { [PSCustomObject]@{ GpoId = $_.GpoId; Target = '" + target + "'; TargetType = 'OU'; Order = $_.Order; Enabled = $_.Enabled } })\n" +
                "$result";
        }
    }
}
