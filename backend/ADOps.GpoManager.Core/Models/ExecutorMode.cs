namespace ADOps.GpoManager.Core.Models;

/// <summary>Mode d'exécution des commandes PowerShell.</summary>
public enum ExecutorMode
{
    /// <summary>Exécution sur la machine locale (nécessite les modules RSAT installés).</summary>
    Local = 0,

    /// <summary>Exécution à distance sur le serveur cible via WinRM (Invoke-Command).</summary>
    Remote = 1,
}
