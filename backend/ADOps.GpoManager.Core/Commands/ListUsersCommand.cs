namespace ADOps.GpoManager.Core.Commands;

/// <summary>
/// Liste les comptes utilisateurs du domaine via Get-ADUser (module ActiveDirectory, RSAT).
/// Les attributs projetés sont ceux exploités par l'interface : identité, OU,
/// département, état du compte et dernière ouverture de session.
/// <para>
/// La classification « administrateur / standard » n'est PAS calculée ici :
/// elle est obtenue par <see cref="ListDomainAdministratorsCommand"/>, puis
/// appliquée par l'analyseur. Exiger <c>memberOf</c> pour chaque compte
/// déclenchait une rétro-liaison très lente qui faisait appearedre
/// l'application bloquée pendant la synchronisation.
/// </para>
/// </summary>
public sealed class ListUsersCommand : PowerShellCommand
{
    public override string Name => "ListUsers";
    public override TimeSpan Timeout { get; } = TimeSpan.FromMinutes(5);

    public override string ScriptBody =>
        """
        Import-Module ActiveDirectory -ErrorAction Stop
        Get-ADUser -Filter * -ErrorAction Stop -Properties Name, DisplayName, GivenName, Surname, SamAccountName, EmailAddress, Title, Department, Enabled, LastLogonDate, lockoutTime, PasswordNeverExpires |
            Select-Object Name, DisplayName, GivenName, Surname, SamAccountName, EmailAddress, Title, Department, Enabled, PasswordNeverExpires, DistinguishedName,
                @{Name='AccountLocked'; Expression={ $null -ne $_.lockoutTime -and $_.lockoutTime -ne 0 -and $_.lockoutTime -ne 9223372036854775807 }},
                @{Name='LastLogonUtc'; Expression={ if ($null -ne $_.LastLogonDate) { $_.LastLogonDate.ToUniversalTime().ToString('o') } else { $null } }} |
            Sort-Object Name
        """;
}
