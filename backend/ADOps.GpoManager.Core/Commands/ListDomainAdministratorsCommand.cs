namespace ADOps.GpoManager.Core.Commands;

/// <summary>
/// Liste les membres des groupes d'administration du domaine (Domain Admins,
/// Enterprise Admins, Administrateurs du contrôleur).
/// <para>
/// Volontairement séparé de <see cref="ListUsersCommand"/> : interroger
/// <c>memberOf</c> pour *chaque* compte est une rétro-liaison très coûteuse
/// (plusieurs minutes sur un domaine de taille moyenne, ce qui faisait
/// appearedre l'application comme bloquée). Interroger directement les
/// groupes d'administration est une requête unique et rapide.
/// </para>
/// </summary>
public sealed class ListDomainAdministratorsCommand : PowerShellCommand
{
    public override string Name => "ListDomainAdministrators";
    public override TimeSpan Timeout { get; } = TimeSpan.FromMinutes(2);

    public override string ScriptBody =>
        """
        Import-Module ActiveDirectory -ErrorAction Stop

        $members = @()
        try {
            $domainSid = (Get-ADDomain -ErrorAction Stop).DomainSID.Value
            # 512 = Domain Admins, 518 = Enterprise Admins, 519 = Administrateurs du DC.
            foreach ($rid in 512, 518, 519) {
                $identity = "$($domainSid)-$rid"
                try {
                    Get-ADGroupMember -Identity $identity -ErrorAction SilentlyContinue |
                        ForEach-Object { if ($null -ne $_.DistinguishedName) { $members += $_.DistinguishedName } }
                } catch {
                    # Groupe absent sur ce contrôleur (cas de 519 hors DC) : sans conséquence.
                }
            }
        } catch {
            $members = @()
        }

        # La forme « objet » est volontairement employée : avec un seul membre,
        # une liste de chaînes serait sérialisée en JSON scalaire
        # (« "CN=…" »), que l'analyseur ne saurait pas lire comme une liste.
        $members | Sort-Object -Unique | ForEach-Object { [PSCustomObject]@{ DistinguishedName = $_ } }
        """;
}
