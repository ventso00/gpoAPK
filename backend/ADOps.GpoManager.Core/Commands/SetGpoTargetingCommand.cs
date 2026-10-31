using System.Text;
using System.Text.RegularExpressions;
using ADOps.GpoManager.Core.Validation;

namespace ADOps.GpoManager.Core.Commands;

/// <summary>
/// Cible réellement une GPO sur un ensemble de machines ou de comptes :
/// crée (si besoin) un groupe de sécurité AD DS, synchronise ses membres,
/// puis remplace le filtrage de sécurité de la GPO par ce groupe.
/// <para>
/// AD DS ne permet pas de lier une GPO directement à un objet ordinateur :
/// le lien reste placé sur l'OU, et le groupe de sécurité restreint
/// l'application aux seuls objets sélectionnés. « Authenticated Users » ne
/// conserve que la lecture, sans application.
/// </para>
/// <para>
/// Le groupe « Domain Computers » reçoit toujours au moins <c>GpoRead</c> :
/// sans ce droit, le poste client ne peut pas télécharger la stratégie, même
/// lorsqu'elle ne contient que des paramètres utilisateur. Pour une GPO de
/// portée Ordinateur (<see cref="ApplyToComputers"/>), il reçoit en plus
/// <c>GpoApply</c> afin que la configuration ordinateur s'applique réellement
/// sur les postes clients du domaine.
/// </para>
/// </summary>
public sealed class SetGpoTargetingCommand : PowerShellCommand
{
    private static readonly Regex GroupNamePattern = new("^[A-Za-z0-9_-]{1,20}$", RegexOptions.Compiled);

    public Guid GpoId { get; }

    public string GroupName { get; }

    public string GroupDescription { get; }

    public IReadOnlyList<string> MemberDistinguishedNames { get; }

    /// <summary>
    /// Vrai pour une GPO de portée Ordinateur : « Domain Computers » reçoit
    /// <c>GpoApply</c>, ce qui force l'application de la configuration
    /// ordinateur sur tous les postes clients du domaine.
    /// </summary>
    public bool ApplyToComputers { get; }

    public override string Name => "SetGpoTargeting";
    public override TimeSpan Timeout { get; } = TimeSpan.FromMinutes(5);

    public SetGpoTargetingCommand(
        Guid gpoId,
        string groupName,
        string groupDescription,
        IReadOnlyList<string> memberDistinguishedNames,
        bool applyToComputers = false)
    {
        if (gpoId == Guid.Empty)
        {
            throw new ArgumentException("L'identifiant (GUID) de la GPO est requis.", nameof(gpoId));
        }

        if (string.IsNullOrWhiteSpace(groupName) || !GroupNamePattern.IsMatch(groupName))
        {
            throw new ArgumentException(
                "Le nom du groupe doit comporter 1 à 20 caractères (lettres, chiffres, tirets, soulignés).",
                nameof(groupName));
        }

        // Une GPO de portée Ordinateur peut être déployée sans compte cible :
        // le filtrage repose alors uniquement sur « Domain Computers ».
        if ((memberDistinguishedNames is null || memberDistinguishedNames.Count == 0) && !applyToComputers)
        {
            throw new ArgumentException("Au moins une cible (ordinateur ou compte) est requise.", nameof(memberDistinguishedNames));
        }

        var members = new List<string>();
        foreach (var dn in memberDistinguishedNames ?? Array.Empty<string>())
        {
            var (isValid, error) = DistinguishedNameValidator.ValidateDistinguishedName(dn);
            if (!isValid)
            {
                throw new ArgumentException(error, nameof(memberDistinguishedNames));
            }

            if (!members.Contains(dn.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                members.Add(dn.Trim());
            }
        }

        GpoId = gpoId;
        GroupName = groupName;
        GroupDescription = string.IsNullOrWhiteSpace(groupDescription)
            ? "Ciblage GPO créé par ADOps"
            : groupDescription.Trim();
        MemberDistinguishedNames = members;
        ApplyToComputers = applyToComputers;
    }

    public override string ScriptBody
    {
        get
        {
            var groupName = Escape(GroupName);
            var description = Escape(GroupDescription);
            var members = string.Join(", ", MemberDistinguishedNames.Select(dn => "'" + Escape(dn) + "'"));
            var groupPermission = ApplyToComputers ? "GpoRead" : "GpoApply";
            var script = new StringBuilder();

            script.AppendLine("Import-Module ActiveDirectory -ErrorAction Stop");
            script.AppendLine("Import-Module GroupPolicy -ErrorAction Stop");
            script.AppendLine("$gpo = Get-GPO -Guid '" + GpoId.ToString() + "' -ErrorAction Stop");

            // Groupe de ciblage : créé dans le conteneur Users s'il n'existe pas.
            script.AppendLine("$group = $null");
            script.AppendLine("try {");
            script.AppendLine("  $group = Get-ADGroup -Identity '" + groupName + "' -ErrorAction Stop");
            script.AppendLine("} catch {");
            script.AppendLine("  $group = $null");
            script.AppendLine("}");
            script.AppendLine("if ($null -eq $group) {");
            script.AppendLine("  $domain = Get-ADDomain -ErrorAction Stop");
            script.AppendLine("  $group = New-ADGroup -Name '" + groupName + "' -SamAccountName '" + groupName
                + "' -GroupScope Global -GroupCategory Security -Path ('CN=Users,' + $domain.DistinguishedName)"
                + " -Description '" + description + "' -PassThru -ErrorAction Stop");
            script.AppendLine("}");
            script.AppendLine("$desired = @(" + members + ")");
            script.AppendLine("$current = @(Get-ADGroupMember -Identity $group -ErrorAction SilentlyContinue | Select-Object -ExpandProperty DistinguishedName)");
            script.AppendLine("foreach ($dn in $current) { if ($desired -notcontains $dn) { Remove-ADGroupMember -Identity $group -Members $dn -Confirm:$false -ErrorAction Stop } }");
            script.AppendLine("$current = @(Get-ADGroupMember -Identity $group -ErrorAction SilentlyContinue | Select-Object -ExpandProperty DistinguishedName)");
            script.AppendLine("foreach ($dn in $desired) { if ($current -notcontains $dn) { Add-ADGroupMember -Identity $group -Members $dn -Confirm:$false -ErrorAction Stop } }");

            // « Authenticated Users » ne garde que la lecture : aucun compte
            // (y compris les administrateurs) ne reçoit la stratégie par défaut.
            // La résolution par SID S-1-5-11 reste valable sur un Windows localisé.
            // « -Replace » est indispensable : sans lui, Set-GPPermission ne
            // rétrograde jamais un droit existant (GpoApply reste GpoApply).
            script.AppendLine("$authUsers = Get-GPPermission -Guid $gpo.Id -All -ErrorAction SilentlyContinue | Where-Object { $_.Trustee.Sid.Value -eq 'S-1-5-11' } | Select-Object -First 1");
            script.AppendLine("if ($null -ne $authUsers) {");
            script.AppendLine("  Set-GPPermission -Guid $gpo.Id -TargetName $authUsers.Trustee.Name -TargetType Group -PermissionLevel GpoRead -Replace -ErrorAction SilentlyContinue");
            script.AppendLine("} else {");
            script.AppendLine("  Set-GPPermission -Guid $gpo.Id -TargetName 'Authenticated Users' -TargetType Group -PermissionLevel GpoRead -Replace -ErrorAction SilentlyContinue");
            script.AppendLine("}");

            // « Domain Computers » : lecture obligatoire pour télécharger la
            // stratégie ; application explicite pour la portée Ordinateur.
            // Le groupe est résolu par son SID (RID 515) pour rester valable
            // sur un contrôleur de domaine localisé.
            script.AppendLine("$domainComputers = $null");
            script.AppendLine("try {");
            script.AppendLine("  $domainSid = (Get-ADDomain -ErrorAction Stop).DomainSID.Value");
            script.AppendLine("  $domainComputers = Get-ADGroup -Identity ($domainSid + '-515') -ErrorAction Stop");
            script.AppendLine("} catch {");
            script.AppendLine("  $domainComputers = Get-ADGroup -Filter \"Name -eq 'Domain Computers' -or SamAccountName -eq 'Domain Computers'\" -ErrorAction SilentlyContinue | Select-Object -First 1");
            script.AppendLine("}");
            script.AppendLine("if ($null -ne $domainComputers) {");
            script.AppendLine("  Set-GPPermission -Guid $gpo.Id -TargetName $domainComputers.Name -TargetType Group -PermissionLevel GpoRead -Replace -ErrorAction SilentlyContinue");
            if (ApplyToComputers)
            {
                script.AppendLine("  Set-GPPermission -Guid $gpo.Id -TargetName $domainComputers.Name -TargetType Group -PermissionLevel GpoApply -Replace -ErrorAction Stop");
            }
            script.AppendLine("} else {");
            script.AppendLine("  Write-Warning 'Groupe Domain Computers introuvable : le filtrage ordinateur peut être incomplet.'");
            script.AppendLine("}");

            // Les administrateurs du domaine conservent leurs droits d'édition
            // (GpoEditDeleteModifySecurity) mais n'obtiennent jamais GpoApply :
            // une stratégie destinée aux utilisateurs standards ne s'applique
            // donc jamais à un compte d'administration.
            script.AppendLine("$domainAdmins = $null");
            script.AppendLine("try {");
            script.AppendLine("  $domainAdmins = Get-ADGroup -Identity ((Get-ADDomain -ErrorAction Stop).DomainSID.Value + '-512') -ErrorAction Stop");
            script.AppendLine("} catch { }");
            script.AppendLine("if ($null -ne $domainAdmins) {");
            script.AppendLine("  Set-GPPermission -Guid $gpo.Id -TargetName $domainAdmins.Name -TargetType Group -PermissionLevel GpoEditDeleteModifySecurity -Replace -ErrorAction SilentlyContinue");
            script.AppendLine("}");

            // Le groupe cible reçoit Apply (portée Utilisateur) ou Read (portée
            // Ordinateur, où l'application est portée par Domain Computers).
            script.AppendLine("Set-GPPermission -Guid $gpo.Id -TargetName '" + groupName + "' -TargetType Group -PermissionLevel " + groupPermission + " -Replace -ErrorAction Stop");
            script.AppendLine("$permissions = @(Get-GPPermission -Guid $gpo.Id -All | Select-Object Trustee, Permission, Inherited)");
            script.AppendLine("[PSCustomObject]@{ GroupName = $group.Name; GroupDn = $group.DistinguishedName; Members = $desired.Count; DomainComputers = $domainComputers.Name; ApplyToComputers = $" + (ApplyToComputers ? "true" : "false") + "; Permissions = $permissions }");

            return script.ToString();
        }
    }

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
