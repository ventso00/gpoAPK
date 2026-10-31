namespace ADOps.GpoManager.Core.Commands;

/// <summary>
/// Commande PowerShell typée. Aucune chaîne saisie par l'utilisateur n'est jamais
/// concaténée librement dans un script : les entrées sont validées par DomainValidator
/// puis injectées par les classes de commandes concrètes (contrainte n°1 du cahier des charges).
/// </summary>
public abstract class PowerShellCommand
{
    /// <summary>Nom court de l'opération, utilisé dans les audits et les journaux.</summary>
    public abstract string Name { get; }

    /// <summary>Corps du script ; les sorties sont converties en JSON par <see cref="BuildScript"/>.</summary>
    public abstract string ScriptBody { get; }

    /// <summary>Délai d'attente maximal de l'exécution.</summary>
    public virtual TimeSpan Timeout { get; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Script complet envoyé à powershell.exe : préambule d'erreur stricte, sortie UTF-8
    /// (pour préserver les accents), et sérialisation JSON pour un parsing fiable.
    /// </summary>
    public string BuildScript()
    {
        // Le corps est exécuté dans un scriptblock « & { … } », et non entre
        // de simples parenthèses.
        //
        // En PowerShell, « ( suite d'instructions ) » n'est pas une expression
        // valide : les parenthèses regroupent une seule expression. Tout corps
        // comportant plus d'une instruction — c'est-à-dire la quasi-totalité des
        // commandes de ce projet (Import-Module suivi de Get-GPO, affectations
        // suivies d'une sérialisation…) — produisait une erreur de syntaxe
        // « Parenthèse fermante manquante », donc un échec avant même l'exécution.
        //
        // « & { … } » est un opérateur d'invocation : il accepte une suite
        // d'instructions et renvoie la valeur de la dernière. Un corps simple
        // continue de se comporter à l'identique.
        return "$ErrorActionPreference = 'Stop'\n" +
               "$ProgressPreference = 'SilentlyContinue'\n" +
               "$OutputEncoding = [System.Text.Encoding]::UTF8\n" +
               "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8\n" +
               "$out = & {\n" + ScriptBody + "\n}\n" +
               "if ($null -ne $out) { $out | ConvertTo-Json -Depth 10 -Compress }";
    }
}
