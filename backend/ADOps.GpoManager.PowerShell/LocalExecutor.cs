using ADOps.GpoManager.Core.Commands;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.PowerShell.Host;

namespace ADOps.GpoManager.PowerShell;

/// <summary>Exécute les commandes PowerShell sur la machine locale (Windows PowerShell 5.1).</summary>
public sealed class LocalExecutor : IPowerShellExecutor
{
    public ExecutorMode Mode => ExecutorMode.Local;

    public Task<PowerShellExecutionResult> ExecuteAsync(PowerShellCommand command, CancellationToken cancellationToken = default)
        => PowerShellProcessRunner.RunAsync(command.BuildScript(), command.Timeout, cancellationToken);
}
