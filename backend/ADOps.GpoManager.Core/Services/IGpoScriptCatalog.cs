using System.Text.Json;

namespace ADOps.GpoManager.Core.Services;

/// <summary>
/// Fournit les scripts PowerShell réels (New-GPO, Set-GPRegistryValue, New-GPLink)
/// associés à chaque GPO du catalogue consolidé <c>backend/gpo/gpo_catalog.json</c>.
/// Le script est exécuté tel quel par l'exécuteur PowerShell (local RSAT ou WinRM),
/// ce qui fait appliquer la GPO réellement sur le domaine.
/// </summary>
public interface IGpoScriptCatalog
{
    /// <summary>Script PowerShell complet pour l'identifiant donné, ou null si inconnu.</summary>
    string? GetFullScript(string gpoId);

    /// <summary>Tous les identifiants disponibles.</summary>
    IReadOnlyList<string> GetIds();
}
