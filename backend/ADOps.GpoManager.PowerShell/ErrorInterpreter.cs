using System.Globalization;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.PowerShell.Host;

namespace ADOps.GpoManager.PowerShell;

/// <summary>Traduit les erreurs PowerShell brutes en erreurs explicites : cause probable + résolution.</summary>
public interface IErrorInterpreter
{
    OperationError Interpret(PowerShellExecutionResult result, string operationName);
}

public sealed class ErrorInterpreter : IErrorInterpreter
{
    /// <summary>Règles de traduction, de la plus spécifique à la plus générique.</summary>
    private static readonly Rule[] Rules =
    {
        // --- Sonde de connectivité préalable (WinRmConnectivityProbe) : ces messages
        // sont produits par l'application elle-même quand DNS/ports sont vérifiés
        // avant toute session WinRM. Ils priment sur les motifs génériques.
        new(
            new[] { "wsman port 5985/5986 unreachable on" },
            "WinRM injoignable sur le poste cible",
            "Le poste répond au DNS mais ses ports WSMan (5985 HTTP / 5986 HTTPS) sont fermés : le service Gestion à distance " +
            "Windows n'est pas activé sur le poste ou son pare-feu le bloque. La vérification gpresult exige ce canal.",
            "Sur le poste, en tant qu'administrateur du domaine : Enable-PSRemoting -Force (ou winrm quickconfig), puis " +
            "vérifier le pare-feu (autoriser le partage de fichiers et la gestion à distance). Alternance sans WinRM : " +
            "gpresult /r peut aussi être lu sur le poste lui-même (console locale) ou via Get-GPResultantSetOfPolicy sur le DC."),

        new(
            new[] { "dns name does not exist: " },
            "Nom du poste non résolu par le DNS",
            "Aucun enregistrement A/AAAA ne correspond au nom de l'ordinateur dans le DNS du domaine : poste éteint et " +
            "enregistrement expiré, poste sorti du domaine, ou nom incomplet (le suffixe primary DNS suffix manque).",
            "Allumer le poste puis sur celui-ci : ipconfig /registerdns. Vérifier dans Utilisateurs et ordinateurs Active " +
            "Directory que l'objet ordinateur est présent et activé, et que son attribut d'adresse IPv4 est renseigné. " +
            "Vérifier aussi la jointure au domaine (Test-ComputerSecureChannel)."),

        // --- Canal WinRM : ces règles précèdent « WinRM non configuré » car un
        // échec de certificat ou de TLS y produit souvent le même message générique.
        new(
            new[] { "certificate chain was issued by an authority that is not trusted", "certificate is not trusted", "untrusted certificate", "certificate chain", "0x80090312", "0x80090308" },
            "Certificat WinRM non reconnu",
            "Le canal WinRM est en HTTPS (port 5986) mais le certificat du contrôleur est auto-signé ou émis par une autorité non approuvée par ce poste. " +
            "C'est le cas par défaut sur une machine virtuelle de test après Enable-PSRemoting : aucun certificat de confiance n'y est installé.",
            "Trois solutions, par ordre de préférence : 1) installer sur le contrôleur un certificat émis par votre autorité, lier son empreinte à HTTP/<fqdn> et conserver « HTTPS strict » ; " +
            "2) ajouter l'autorité au magasin « Autorités de certification racines de confiance » du poste d'administration ; " +
            "3) à défaut, choisir « Négocier » dans Paramètres ▸ Canal WinRM : l'application basculera sur HTTP 5985 en signalant explicitement le repli."),

        new(
            new[] { "could not use ssl", "ssl connection", "secure channel" },
            "Canal HTTPS WinRM indisponible",
            "La négociation TLS avec le contrôleur a échoué sur le port 5986 : l'écouteur HTTPS est absent, ou les versions de TLS proposées ne sont plus acceptées.",
            "Créer l'écouteur HTTPS sur le contrôleur (New-Item -Path WSMan:\\localhost\\Listener -Transport HTTPS -Address * puis lui associer un certificat), " +
            "ou choisir « Négocier » dans Paramètres ▸ Canal WinRM pour utiliser HTTP 5985."),

        new(
            new[] { "kerberos authentication is not supported", "0x8009030e" },
            "Authentification Kerberos impossible",
            "Le contrôleur n'a pas pu authentifier le compte : le SPN HTTP/<fqdn> est absent, ou l'horloge du poste s'écarte trop de celle du contrôleur pour émettre un jeton valide.",
            "Vérifier que le poste est joint au domaine et que l'horloge est synchronisée avec le contrôleur (w32tm /query /status puis w32tm /resync). " +
            "Sur une plateforme de test, l'accès à distance n'est pas indispensable : le mode local (RSAT) évite complètement ce chemin."),

        new(
            new[]
            {
                // Anglais
                "the client cannot connect to the destination specified in the request", "winrm cannot process the request", "the winrm client",
                // Français : sur un Windows localisé, les messages de Test-WSMan / Invoke-Command
                // sont traduits. Sans ces motifs, la règle ne se déclenche pas et l'utilisateur
                // ne voit qu'un « code de sortie 1 » muet.
                "le client winrm ne peut pas", "winrm ne peut pas traiter", "impossible de se connecter à la destination",
                "la connexion à la destination", "le service winrm", "gestion à distance windows",
            },
            "WinRM non configuré ou injoignable",
            "Le service Gestion à distance Windows (WinRM) n'accepte pas les requêtes sur la cible : service arrêté, listener absent ou pare-feu (port 5985/5986).",
            "Sur le serveur cible, exécuter en tant qu'administrateur : Enable-PSRemoting -Force (ou winrm quickconfig)  —  puis vérifier que le service « Gestion à distance Windows (WinRM) » " +
            "est démarré et ouvrir le port 5985 (HTTP) / 5986 (HTTPS) dans le pare-feu. Note : requis UNE FOIS par machine. " +
            "Alternative sans WinRM : installer RSAT sur ce poste d'administration (Add-WindowsCapability -Online -Name Rsat.GroupPolicy.Management~~~~0.0.1.0) — les commandes s'exécuteront alors en local."),

        new(
            new[]
            {
                "rpc server is unavailable", "0x800706ba",
                "le serveur rpc n'est pas disponible", "serveur rpc", "0x800706ba",
            },
            "Serveur RPC indisponible",
            "Le contrôleur de domaine ne répond pas sur le port RPC (TCP 135 + ports dynamiques) : machine éteinte, réseau mal configuré ou pare-feu.",
            "Vérifier que le serveur cible est allumé et accessible (Test-Connection <fqdn>), ouvrir les ports RPC dans le pare-feu (TCP 135 et 49152-65535) " +
            "et s'assurer que le service « Appel de procédure distante (RPC) » est démarré sur la cible."),

        new(
            new[]
            {
                "the term 'get-gpo' is not recognized", "no valid module file was found", "the specified module 'grouppolicy' was not loaded",
                "n'est pas reconnu comme nom d'applet de commande", "n'est pas reconnu en tant que nom", "module « grouppolicy »", "module 'grouppolicy'", "fichier de module valide introuvable",
            },
            "Module GroupPolicy introuvable",
            "La fonctionnalité RSAT « Gestion des stratégies de groupe » (GPMC) n'est pas installée sur cette machine.",
            "Installer le module : Paramètres ▸ Applications ▸ Fonctionnalités facultatives ▸ Ajouter « Outils d'administration de serveur distant › Gestion des stratégies de groupe ». " +
            "Ou en PowerShell (administrateur) : Add-WindowsCapability -Online -Name Rsat.GroupPolicy.Management~~~~0.0.1.0  —  Sur Windows Server : Install-WindowsFeature GPMC"),

        new(
            new[] { "access is denied", "0x80070005", "accès refusé", "access refus" },
            "Accès refusé",
            "Le compte courant n'a pas les droits d'administration sur la cible, ou n'appartient pas au groupe « Remote Management Users » / « Administrateurs » du domaine.",
            "Exécuter l'application avec un compte disposant des droits d'administration sur le serveur cible (ex. : administrateur du domaine) " +
            "et ajouter le compte au groupe « Remote Management Users » sur la cible (lusrmgr.msc ou Add-LocalGroupMember -Group \"Remote Management Users\" -Member <compte>)."),

        new(
            new[] { "the network path was not found", "0x80070035" },
            "Chemin réseau introuvable",
            "La machine cible est injoignable sur le réseau (adresse IP, DNS ou routage).",
            "Vérifier la connectivité réseau vers la cible (Test-Connection <fqdn>) et la configuration IP/DNS du poste."),

        new(
            new[]
            {
                "dns name does not exist", "non-existent domain", "no such host is known", "the requested name is valid, but no data of the requested type was found", "dns query failed",
                "le nom dns n'existe pas", "domaine inexistant", "aucun hôte connu", "aucune information trouvée", "la requête dns a échoué",
            },
            "Échec de la résolution DNS",
            "Le nom DNS du serveur ne peut pas être résolu : le serveur DNS ne connaît pas l'enregistrement ou le suffixe DNS est incorrect.",
            "Vérifier l'orthographe du FQDN, que la machine cible est enregistrée dans le DNS (ipconfig /registerdns sur la cible) " +
            "et que le serveur DNS du domaine est utilisé (nslookup <fqdn>)."),

        new(
            new[]
            {
                "cannot find domain controller", "no domain controller could be found", "the domain controller could not be found", "the specified domain does not exist or could not be contacted",
                "contrôleur de domaine introuvable", "aucun contrôleur de domaine", "le domaine spécifié n'existe pas", "n'a pas pu être contacté",
            },
            "Contrôleur de domaine introuvable",
            "Le domaine spécifié n'existe pas depuis ce poste, ou aucun contrôleur de domaine ne répond.",
            "Vérifier que le poste est joint au domaine (ou que le DNS du domaine est configuré en serveur DNS préféré) et que les contrôleurs de domaine sont joignables."),

        new(
            new[] { "the trust relationship between this workstation and the primary domain failed", "0x800706f4" },
            "Relation d'approbation rompue",
            "Le compte machine du poste n'est plus approuvé par le domaine.",
            "Réintégrer le poste dans le domaine (Add-Computer) ou réinitialiser le compte machine dans Active Directory."),

        new(
            new[] { "the operation has timed out", "the request timed out" },
            "Délai d'attente dépassé",
            "L'opération a pris trop de temps : hôte lent, recherche DNS lente ou pare-feu qui laisse les connexions s'ouvrir mais les bloque ensuite.",
            "Vérifier la latence réseau (Test-Connection <fqdn> -Count 5) et réessayer."),
    };

    public OperationError Interpret(PowerShellExecutionResult result, string operationName)
    {
        if (result.TimedOut)
        {
            return new OperationError(
                "Délai d'attente dépassé",
                "L'opération « " + operationName + " » n'a pas terminé dans les " + result.Duration.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " secondes imparties.",
                "Le serveur est peut-être joignable mais lent, ou un pare-feu bloque silencieusement le trafic. Vérifiez la latence réseau (Test-Connection <fqdn>) puis réessayez.",
                result.StandardError);
        }

        var stderr = result.StandardError ?? string.Empty;
        var normalized = stderr.ToLowerInvariant();

        foreach (var rule in Rules)
        {
            if (rule.Patterns.Any(pattern => normalized.Contains(pattern, StringComparison.Ordinal)))
            {
                return new OperationError(rule.Title, rule.Cause, rule.Remediation, stderr);
            }
        }

        var firstLine = stderr.Split('\n').FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim()
            ?? "(aucune sortie d'erreur)";

        return new OperationError(
            "Échec de l'opération « " + operationName + " » (code de sortie " + result.ExitCode.ToString(CultureInfo.InvariantCulture) + ")",
            "PowerShell a signalé : " + firstLine,
            "Consulter le fichier journal (logs/adops.log) pour le détail complet de l'erreur.",
            stderr);
    }

    private sealed record Rule(string[] Patterns, string Title, string Cause, string Remediation);
}