using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Security;

namespace ADOps.GpoManager.Infrastructure.Configuration;

/// <summary>Configuration applicative persistante (settings.json) — aucun credential.</summary>
public sealed record AppConfiguration
{
    /// <summary>
    /// Contrôleur de domaine cible (FQDN de préférence : Kerberos exige le
    /// SPN « HTTP/&lt;fqdn&gt; » ; une adresse IP n'a pas de SPN).
    /// </summary>
    public string ServerFqdn { get; init; } = "ventso.sarytany.local";

    /// <summary>
    /// Adresse IP du contrôleur. Sert de repli lorsque le FQDN ne se résout
    /// pas (DNS de la VM mal configuré) et de contrôle de connectivité.
    /// </summary>
    public string ServerAddress { get; init; } = "192.168.5.2";

    /// <summary>Nom de domaine Active Directory (nom DNS complet).</summary>
    public string DomainFqdn { get; init; } = "sarytany.local";

    /// <summary>Nom NetBIOS du domaine (facultatif).</summary>
    public string? DomainNetbios { get; init; } = "SARYTANY";

    /// <summary>
    /// Mode d'exécution des commandes. « Local » (RSAT) est le mode recommandé :
    /// les commandes utilisent le compte Windows de la session.
    /// </summary>
    public ExecutorMode ExecutorMode { get; init; } = ExecutorMode.Local;

    /// <summary>
    /// Synchronisation automatique périodique. La synchronisation de démarrage
    /// est, elle, toujours exécutée.
    /// </summary>
    public bool AutoSyncEnabled { get; init; } = true;

    /// <summary>Intervalle entre deux synchronisations automatiques (minutes).</summary>
    public int SyncIntervalMinutes { get; init; } = 15;

    public int LogMaxFileSizeMb { get; init; } = 5;
    public int LogFilesToKeep { get; init; } = 5;

    /// <summary>Options de durcissement (HTTPS WinRM, verrouillage de session, hygiène des journaux).</summary>
    public SecurityOptions Security { get; init; } = new();
}
