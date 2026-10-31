using ADOps.GpoManager.Core.Commands;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.PowerShell.Host;

namespace ADOps.GpoManager.PowerShell.Host;

/// <summary>Exécute une commande PowerShell typée et retourne son résultat brut.</summary>
public interface IPowerShellExecutor
{
    ExecutorMode Mode { get; }
    Task<PowerShellExecutionResult> ExecuteAsync(PowerShellCommand command, CancellationToken cancellationToken = default);
}

/// <summary>Fabrique d'exécuteurs : local (RSAT) et distant (WinRM, éventuellement avec compte explicite).</summary>
public interface IPowerShellExecutorFactory
{
    IPowerShellExecutor Local { get; }
    IPowerShellExecutor CreateRemote(string targetFqdn, ConnectionCredentials? credentials = null);
}
