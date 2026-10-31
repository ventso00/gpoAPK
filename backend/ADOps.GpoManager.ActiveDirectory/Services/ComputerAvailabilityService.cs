using System.Text.Json;
using System.Threading;
using ADOps.GpoManager.Core.Commands;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Services;
using ADOps.GpoManager.Core.Validation;
using ADOps.GpoManager.PowerShell;
using ADOps.GpoManager.PowerShell.Host;

namespace ADOps.GpoManager.ActiveDirectory;

/// <summary>
/// Disponibilité des ordinateurs : résolution DNS puis test WinRM (Test-WSMan).
/// ICMP n'est pas utilisé : un ping échoué ne prouve pas qu'une machine est éteinte.
/// Les tests tournent en parallèle bornée et sont annulables.
/// </summary>
public sealed class ComputerAvailabilityService : IComputerAvailabilityService
{
    private readonly IPowerShellExecutorFactory _executors;
    private readonly PowerShellOutputParser _jsonParser = new();
    private readonly ILogger _logger;

    private const int MaxDegreeOfParallelism = 8;

    public ComputerAvailabilityService(IPowerShellExecutorFactory executors, ILogger logger)
    {
        _executors = executors;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ComputerHealthInfo>> CheckAsync(
        IReadOnlyList<ComputerInfo> computers,
        DomainContext context,
        CancellationToken cancellationToken = default)
    {
        var executor = context.ExecutorMode == ExecutorMode.Remote
            ? _executors.CreateRemote(context.ServerFqdn, context.Credentials)
            : _executors.Local;
        var computerList = computers.ToList();
        var results = new ComputerHealthInfo[computerList.Count];

        var options = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Min(MaxDegreeOfParallelism, Math.Max(1, computerList.Count)),
        };

        await Parallel.ForAsync(0, computerList.Count, options, async (index, ct) =>
        {
            var computer = computerList[index];
            results[index] = await CheckOneAsync(executor, computer, ct);
        });

        return results;
    }

    private async Task<ComputerHealthInfo> CheckOneAsync(IPowerShellExecutor executor, ComputerInfo computer, CancellationToken cancellationToken)
    {
        var checkedAt = DateTimeOffset.UtcNow;

        // Le nom de l'ordinateur provient d'AD DS : il est validé avant injection.
        var (isValid, _) = DomainValidator.ValidateServerFqdn(computer.Name);
        if (!isValid)
        {
            return new ComputerHealthInfo
            {
                ComputerName = computer.Name,
                ComputerDistinguishedName = computer.DistinguishedName,
                State = AvailabilityState.Unknown,
                Method = AvailabilityCheckMethod.None,
                CheckedAtUtc = checkedAt,
                Error = "Nom d'ordinateur non valide pour un test réseau.",
            };
        }

        var fqdn = DomainValidator.NormalizeFqdn(computer.Name);

        // 1. Résolution DNS. La résolution locale (.NET) est tentée en premier :
        // elle ne dépend pas de WinRM et fonctionne dès que le poste joint le
        // domaine. Le secours distant (Resolve-DnsName sur le DC) n'est utilisé
        // que si le cache/le resolver local ne répondent pas.
        string? ipAddress = await ResolveAddressLocallyAsync(fqdn).ConfigureAwait(false);
        if (ipAddress is null)
        {
            try
            {
                var dnsCommand = new SimplePowerShellCommand(
                    "DnsResolution",
                    "Resolve-DnsName -Name '" + fqdn + "' -Type A -ErrorAction Stop | Select-Object -First 1 Name, IPAddress",
                    TimeSpan.FromSeconds(15));
                var dnsResult = await executor.ExecuteAsync(dnsCommand, cancellationToken);
                if (dnsResult.ExitCode == 0 && !dnsResult.TimedOut)
                {
                    var element = _jsonParser.ParseRaw(dnsResult.StandardOutput);
                    ipAddress = ReadStringProperty(element, "IPAddress");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Debug("DNS check failed for " + fqdn, ex);
            }
        }

        // Déclarative AD DS (attribut IPv4Address) : dernière source, last-known-good.
        var effectiveIp = ipAddress ?? computer.IpAddress;

        if (effectiveIp is null)
        {
            return new ComputerHealthInfo
            {
                ComputerName = computer.Name,
                ComputerDistinguishedName = computer.DistinguishedName,
                State = AvailabilityState.DnsUnresolved,
                Method = AvailabilityCheckMethod.Dns,
                DnsResolved = false,
                CheckedAtUtc = checkedAt,
                Error = "Le nom n'a pas été résolu par le DNS et l'objet AD DS ne déclare aucune IPv4 (la machine peut néanmoins fonctionner : le DNS peut être incomplet ou la résolution courte).",
            };
        }

        // 2. WinRM (Test-WSMan) — prouve aussi que le réseau est joignable sur 5985
        try
        {
            var winRmCommand = new SimplePowerShellCommand(
                "WinRmService",
                "Test-WSMan -ComputerName '" + fqdn + "' -ErrorAction Stop | Select-Object ProductVendor",
                TimeSpan.FromSeconds(45));
            var winRmResult = await executor.ExecuteAsync(winRmCommand, cancellationToken);
            var winRmAvailable = winRmResult.ExitCode == 0 && !winRmResult.TimedOut;

            return new ComputerHealthInfo
            {
                ComputerName = computer.Name,
                ComputerDistinguishedName = computer.DistinguishedName,
                State = winRmAvailable ? AvailabilityState.Online : AvailabilityState.OnlineWithoutWinRm,
                Method = AvailabilityCheckMethod.DnsAndWinRm,
                DnsResolved = true,
                DnsIpAddress = effectiveIp,
                WinRmAvailable = winRmAvailable,
                CheckedAtUtc = checkedAt,
                Error = winRmAvailable
                    ? null
                    : "DNS résolu mais WinRM ne répond pas : le poste est joignable, mais la gestion à distance est indisponible (WinRM arrêté ou pare-feu, port 5985). Un ping échoué ne signifierait rien de plus.",
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Debug("WinRM check failed for " + fqdn, ex);
            return new ComputerHealthInfo
            {
                ComputerName = computer.Name,
                ComputerDistinguishedName = computer.DistinguishedName,
                State = AvailabilityState.OnlineWithoutWinRm,
                Method = AvailabilityCheckMethod.DnsAndWinRm,
                DnsResolved = true,
                DnsIpAddress = effectiveIp,
                WinRmAvailable = false,
                CheckedAtUtc = checkedAt,
                Error = "DNS résolu, test WinRM impossible : " + ex.Message,
            };
        }
    }

    private static async Task<string?> ResolveAddressLocallyAsync(string fqdn)
    {
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var addresses = await System.Net.Dns.GetHostAddressesAsync(fqdn, timeoutCts.Token).ConfigureAwait(false);
            return addresses
                .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(a => a.ToString())
                .FirstOrDefault()
                ?? addresses.FirstOrDefault()?.ToString();
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or OperationCanceledException)
        {
            return null;
        }
    }

    private static string? ReadStringProperty(JsonElement? element, string propertyName)
    {
        if (element is null)
        {
            return null;
        }

        if (element.Value.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }
}
