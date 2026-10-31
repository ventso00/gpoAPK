using System.Globalization;
using ADOps.GpoManager.Core.Validation;

namespace ADOps.GpoManager.Core.Commands;

/// <summary>
/// Configure le filtrage de sécurité d'une GPO : autorise uniquement un groupe
/// spécifique (groupe de comptes standards) et exclut explicitement les
/// administrateurs du domaine. Cela garantit qu'une GPO de portée Utilisateur
/// ne s'applique jamais aux comptes d'administration, même sur le même poste.
/// </summary>
public sealed class SetGpoSecurityFilterCommand : PowerShellCommand
{
    public Guid GpoId { get; }
    public string AllowedGroupDistinguishedName { get; }
    public string ExcludedGroupDistinguishedName { get; }
    public bool Enforced { get; }

    public SetGpoSecurityFilterCommand(
        Guid gpoId,
        string allowedGroupDistinguishedName,
        string excludedGroupDistinguishedName,
        bool enforced = false)
    {
        if (gpoId == Guid.Empty)
        {
            throw new ArgumentException("L'identifiant (GUID) de la GPO est requis.", nameof(gpoId));
        }

        var (allowedValid, allowedError) = DistinguishedNameValidator.ValidateDistinguishedName(allowedGroupDistinguishedName);
        if (!allowedValid)
        {
            throw new ArgumentException(allowedError, nameof(allowedGroupDistinguishedName));
        }

        if (!string.IsNullOrWhiteSpace(excludedGroupDistinguishedName))
        {
            var (excludedValid, excludedError) = DistinguishedNameValidator.ValidateDistinguishedName(excludedGroupDistinguishedName);
            if (!excludedValid)
            {
                throw new ArgumentException(excludedError, nameof(excludedGroupDistinguishedName));
            }
        }

        GpoId = gpoId;
        AllowedGroupDistinguishedName = allowedGroupDistinguishedName.Trim();
        ExcludedGroupDistinguishedName = string.IsNullOrWhiteSpace(excludedGroupDistinguishedName) ? null : excludedGroupDistinguishedName.Trim();
        Enforced = enforced;
    }

    public override string Name => "SetGpoSecurityFilter";
    public override TimeSpan Timeout { get; } = TimeSpan.FromMinutes(2);

    public override string ScriptBody
    {
        get
        {
            var allowed = EscapeLiteral(AllowedGroupDistinguishedName);
            var script = new System.Text.StringBuilder();
            
            script.AppendLine("Import-Module GroupPolicy -ErrorAction Stop");
            script.AppendLine("Import-Module ActiveDirectory -ErrorAction Stop");
            
            // Récupère les permissions actuelles de la GPO
            script.AppendLine("$currentPermissions = @()");
            script.AppendLine("try {");
            script.AppendLine("    $gpo = Get-GPO -Guid '" + GpoId.ToString() + "' -ErrorAction Stop");
            script.AppendLine("    if ($gpo.SecurityFiltering) {");
            script.AppendLine("        $currentPermissions = @($gpo.SecurityFiltering | Where-Object { $_.Active -and $_.Permission -ne 'BypassUsers' })");
            script.AppendLine("    }");
            script.AppendLine("} catch { }");
            
            // Ajoute le groupe autorisé avec FullControl
            script.AppendLine("$currentPermissions += New-Object Microsoft.GroupPolicy.Management.PermissionData('Gpo', '" + allowed + "', 'FullControl', $false)");
            
            // Exclut le groupe administrateurs si spécifié
            if (!string.IsNullOrEmpty(ExcludedGroupDistinguishedName))
            {
                var excluded = EscapeLiteral(ExcludedGroupDistinguishedName);
                script.AppendLine("$currentPermissions += New-Object Microsoft.GroupPolicy.Management.PermissionData('Gpo', '" + excluded + "', 'FullControl', $true)");
            }
            
            script.AppendLine("Set-GPPermission -Guid '" + GpoId.ToString() + "' -PermissionEntry $currentPermissions -ErrorAction Stop");
            
            if (Enforced)
            {
                script.AppendLine("Set-GPLink -Guid '" + GpoId.ToString() + "' -Target \"\" -Enforced Yes -ErrorAction Stop");
            }
            
            // Affiche le résultat pour vérification
            script.AppendLine("$gpo = Get-GPO -Guid '" + GpoId.ToString() + "' -ErrorAction Stop");
            script.AppendLine("$gpo.SecurityFiltering | Select-Object IdentityReference, Permission, Active, Inherited, Enforced | ConvertTo-Json -Depth 3 -Compress");

            return script.ToString();
        }
    }

    private static string EscapeLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}