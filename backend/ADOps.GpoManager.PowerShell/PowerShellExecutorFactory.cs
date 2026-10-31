using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Security;
using ADOps.GpoManager.Core.Services;
using ADOps.GpoManager.PowerShell.Host;

namespace ADOps.GpoManager.PowerShell;

public sealed class PowerShellExecutorFactory : IPowerShellExecutorFactory
{
    private readonly LocalExecutor _local = new();
    private readonly ILogger? _logger;
    private WinRmTransportMode _transport = WinRmTransportMode.Auto;

    /// <param name="logger">
    /// Transmis aux exécuteurs distants pour journaliser tout repli de
    /// HTTPS vers HTTP (un passage en clair doit rester traçable).
    /// </param>
    public PowerShellExecutorFactory(ILogger? logger = null) => _logger = logger;

    /// <summary>
    /// Canal WinRM appliqué à tous les exécut distants créés ensuite.
    /// Modifiable à chaud : un changement depuis l'écran « Paramètres »
    /// s'applique au prochain Health Check, sans redémarrage.
    /// </summary>
    public WinRmTransportMode Transport
    {
        get => _transport;
        set => _transport = value;
    }

    /// <summary>
    /// Raccourci de compatibilité avec l'ancien réglage booléen.
    /// Prefère <see cref="Transport"/>, qui distingue « HTTPS strict »
    /// de « HTTPS avec repli HTTP ».
    /// </summary>
    public bool RequireHttps
    {
        get => _transport != WinRmTransportMode.Http;
        set => _transport = value ? WinRmTransportMode.Https : WinRmTransportMode.Http;
    }

    public IPowerShellExecutor Local => _local;

    public IPowerShellExecutor CreateRemote(string targetFqdn, ConnectionCredentials? credentials = null)
        => new RemoteExecutor(targetFqdn, credentials, Transport, _logger);
}