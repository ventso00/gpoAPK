using ADOps.GpoManager.Core.Validation;

namespace ADOps.GpoManager.Core.Commands;

/// <summary>
/// Test d'authentification et de sécurité :
/// - identité obtenue sur la cible (Invoke-Command) — le compte explicite est
///   transporté par le blob DPAPI de l'exécuteur distant (jamais en clair) ;
/// - rôle administrateur obtenu sur la cible ;
/// - les erreurs réseau / d'identification / de droits sont distinguées par le
///   moteur à partir du code de sortie et de stderr (aucun mot de passe journalisé).
/// </summary>
public sealed class DiagnoseAuthenticationCommand : PowerShellCommand
{
    public string ServerFqdn { get; }

    public override string Name => "DiagnoseAuthentication";
    public override TimeSpan Timeout { get; } = TimeSpan.FromSeconds(90);

    public DiagnoseAuthenticationCommand(string serverFqdn)
    {
        var (isValid, error) = DomainValidator.ValidateServerFqdn(serverFqdn);
        if (!isValid)
        {
            throw new ArgumentException(error, nameof(serverFqdn));
        }

        ServerFqdn = DomainValidator.NormalizeFqdn(serverFqdn);
    }

    public override string ScriptBody
    {
        get
        {
            var server = DistinguishedNameValidator.EscapePowerShellLiteral(ServerFqdn);

            // Le script intérieur est transporté par Invoke-Command : il produit le JSON
            // de session sur la cible. Aucun credential n'apparaît dans ce script.
            return
                "$server = '" + server + "'\n" +
                "$identity = Invoke-Command -ComputerName $server -ErrorAction Stop -ScriptBlock {\n" +
                "    $isAdmin = $false\n" +
                "    try {\n" +
                "        $current = [Security.Principal.WindowsIdentity]::GetCurrent()\n" +
                "        $principal = New-Object Security.Principal.WindowsPrincipal($current)\n" +
                "        $isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)\n" +
                "    } catch {}\n" +
                "    @{ UserName = $env:USERNAME; Domain = $env:USERDOMAIN; IsAdministrator = $isAdmin }\n" +
                "}\n" +
                "@{ UserName = $identity.UserName; Domain = $identity.Domain; IsAdministrator = [bool]$identity.IsAdministrator; Success = $true }";
        }
    }
}
