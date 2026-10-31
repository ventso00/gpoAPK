using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ADOps.GpoManager.Core.Commands;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Services;
using ADOps.GpoManager.Core.Validation;
using ADOps.GpoManager.PowerShell;
using ADOps.GpoManager.PowerShell.Host;

/// <summary>
/// Découverte des machines du masque réseau du serveur cible :
/// - mode local (RSAT) : balayage ICMP /24 en parallèle (C#) + DNS inverse ;
/// - mode distant (WinRM) : balayage exécuté sur le contrôleur de domaine
///   (DiscoverSubnetHostsCommand, pool de runspaces) ;
/// puis croisement avec l'inventaire AD DS (objets ordinateurs connus).
/// Un ping échoué ne prouve pas qu'une machine est éteinte : ICMP est
/// souvent bloqué. Les machines connues d'AD DS mais non joignables
/// sont affichées « hors ligne », jamais supprimées.
/// </summary>
public sealed class NetworkDiscoveryService : INetworkDiscoveryService
{
    private const int PingTimeoutMilliseconds = 1000;
    private const int SweepDegreeOfParallelism = 64;

    /// <summary>
    /// Masque de sous-réseau par défaut lorsqu'aucune interface réseau n'est
    /// identifiable. Utilisé comme valeur de repli lorsque le masque réel ne
    /// peut être déterminé.
    /// </summary>
    private const int DefaultSubnetMask = 24;

    private readonly IPowerShellExecutorFactory _executors;
    private readonly PowerShellOutputParser _parser = new();
    private readonly IErrorInterpreter _interpreter;
    private readonly IAuditService _audit;
    private readonly ILogger _logger;

    public NetworkDiscoveryService(
        IPowerShellExecutorFactory executors,
        IErrorInterpreter interpreter,
        IAuditService audit,
        ILogger logger)
    {
        _executors = executors;
        _interpreter = interpreter;
        _audit = audit;
        _logger = logger;
    }

    public async Task<OperationResult<IReadOnlyList<NetworkHostInfo>>> DiscoverAsync(
        DomainContext context,
        IReadOnlyList<ComputerInfo> knownComputers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        knownComputers ??= Array.Empty<ComputerInfo>();
        var checkedAt = DateTimeOffset.UtcNow;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // 1. Adresse IPv4 du serveur cible (littérale ou résolue via DNS)
            var serverAddress = await ResolveServerAddressAsync(context, cancellationToken);
            // Récupération dynamique du masque de sous-réseau plutôt que d'assumer /24
            var subnetMask = GetSubnetMask(serverAddress);
            var networkPrefix = GetNetworkPrefix(serverAddress, subnetMask);

            _logger.Information(
                "NetworkDiscovery | cible=" + context.ServerFqdn
                + " | adresse=" + serverAddress
                + " | masque/CIDR=" + subnetMask
                + " | mode=" + context.ExecutorMode);

            // 2. Balayage du masque réseau (local ou sur le DC)
            var hostCount = GetHostCountFromSubnetMask(subnetMask);
            var discovered = context.ExecutorMode == ExecutorMode.Remote
                ? await DiscoverRemoteAsync(context, networkPrefix, hostCount, cancellationToken)
                : await DiscoverLocalAsync(networkPrefix, hostCount, cancellationToken);

            // 3. Croisement avec l'inventaire AD DS
            var hosts = MergeWithActiveDirectory(discovered, knownComputers, checkedAt);

            var onlineCount = hosts.Count(host => host.Online);
            stopwatch.Stop();
            await _audit.LogAsync(
                AuditActions.NetworkDiscovery,
                context.ServerFqdn,
                AuditResult.Success,
                "Découverte du sous-réseau " + networkPrefix + "0/" + subnetMask + " : "
                    + onlineCount + " machine(s) en ligne, "
                    + hosts.Count(host => host.IsAdComputer) + " connue(s) d'AD DS.",
                null,
                new AuditContext(
                    Server: context.ServerFqdn,
                    Domain: context.DomainFqdn,
                    Duration: stopwatch.Elapsed,
                    OperationId: Guid.NewGuid().ToString("N")));

            return OperationResult<IReadOnlyList<NetworkHostInfo>>.Success(hosts);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await _audit.LogAsync(
                AuditActions.NetworkDiscovery,
                context.ServerFqdn,
                AuditResult.Warning,
                "Découverte du sous-réseau annulée par l'utilisateur.");
            return OperationResult<IReadOnlyList<NetworkHostInfo>>.Failure(
                new OperationError("Opération annulée", "L'utilisateur a annulé l'opération."));
        }
        catch (Exception ex)
        {
            _logger.Error("Découverte du sous-réseau échouée", ex);
            await _audit.LogAsync(
                AuditActions.NetworkDiscovery,
                context.ServerFqdn,
                AuditResult.Failure,
                "Échec de la découverte du sous-réseau : " + ex.Message,
                ex.ToString(),
                new AuditContext(
                    Server: context.ServerFqdn,
                    Domain: context.DomainFqdn,
                    OperationId: Guid.NewGuid().ToString("N")));
            return OperationResult<IReadOnlyList<NetworkHostInfo>>.Failure(new OperationError(
                "Découverte du sous-réseau impossible",
                ex.Message,
                "Vérifiez que le serveur cible répond et que le mode d'exécution est adapté (RSAT local ou WinRM)."));
        }
    }

    /// <summary>
    /// Balayage local : ICMP en parallèle avec plage d'adresses calculée
    /// en fonction du masque de sous-réseau réel, puis résolution DNS inverse
    /// de chaque hôte en ligne.
    /// </summary>
    private static async Task<IReadOnlyList<NetworkHostInfo>> DiscoverLocalAsync(
        string networkPrefix,
        int hostCount,
        CancellationToken cancellationToken)
    {
        var online = new ConcurrentBag<NetworkHostInfo>();
        var checkedAt = DateTimeOffset.UtcNow;

        await Parallel.ForEachAsync(
            Enumerable.Range(1, hostCount),
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = SweepDegreeOfParallelism,
            },
            async (lastOctet, token) =>
            {
                var ipAddress = networkPrefix + lastOctet;
                try
                {
                    using var ping = new Ping();
                    var reply = await ping.SendPingAsync(ipAddress, PingTimeoutMilliseconds)
                        .ConfigureAwait(false);
                    if (reply.Status != IPStatus.Success)
                    {
                        return;
                    }

                    string? hostName = null;
                    try
                    {
                        hostName = (await Dns.GetHostEntryAsync(ipAddress).ConfigureAwait(false)).HostName;
                    }
                    catch (SocketException)
                    {
                        // Pas de résolution inverse : l'hôte est en ligne mais anonyme.
                    }

                    online.Add(new NetworkHostInfo
                    {
                        IpAddress = ipAddress,
                        HostName = hostName,
                        Online = true,
                        Origin = NetworkHostOrigin.Discovered,
                        CheckedAtUtc = checkedAt,
                    });
                }
                catch (PingException)
                {
                    // Hôte injoignable : le silence ICMP n'est pas une erreur.
                }
                catch (SocketException)
                {
                    // Idem.
                }
            });

        return online
            .OrderBy(host => host.HostName ?? host.IpAddress, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Balayage distant : la commande typée s'exécute sur le contrôleur de domaine.</summary>
    private async Task<IReadOnlyList<NetworkHostInfo>> DiscoverRemoteAsync(
        DomainContext context,
        string networkPrefix,
        int hostCount,
        CancellationToken cancellationToken)
    {
        var executor = _executors.CreateRemote(context.ServerFqdn, context.Credentials);
        var command = new DiscoverSubnetHostsCommand(networkPrefix, hostCount);
        var result = await executor.ExecuteAsync(command, cancellationToken);

        if (result.ExitCode != 0 || result.TimedOut)
        {
            var error = _interpreter.Interpret(result, "Découverte du sous-réseau");
            throw new InvalidOperationException(error.ToDisplayString());
        }

        var hosts = new List<NetworkHostInfo>();
        var checkedAt = DateTimeOffset.UtcNow;
        foreach (var element in EnumerateDiscoveredElements(result.StandardOutput))
        {
            var ipAddress = ReadString(element, "IpAddress");
            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                continue;
            }

            hosts.Add(new NetworkHostInfo
            {
                IpAddress = ipAddress,
                HostName = ReadString(element, "HostName"),
                Online = true,
                Origin = NetworkHostOrigin.Discovered,
                CheckedAtUtc = checkedAt,
            });
        }

        return hosts;
    }

    /// <summary>
    /// Croisement : chaque hôte découvert est rapproché des objets
    /// ordinateurs d'AD DS (par nom, ou par adresse IP résolue depuis
    /// le nom de l'ordinateur). Les ordinateurs d'AD DS non découverts
    /// sont conservés comme « hors ligne ».
    /// </summary>
    private IReadOnlyList<NetworkHostInfo> MergeWithActiveDirectory(
        IReadOnlyList<NetworkHostInfo> discovered,
        IReadOnlyList<ComputerInfo> knownComputers,
        DateTimeOffset checkedAt)
    {
        var hosts = new List<NetworkHostInfo>();
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Index des hôtes par adresse IP (pour le croisement par résolution directe).
        var hostsByIp = discovered
            .Where(host => !string.IsNullOrWhiteSpace(host.IpAddress))
            .ToDictionary(host => host.IpAddress!, host => host, StringComparer.OrdinalIgnoreCase);

        foreach (var host in discovered)
        {
            var computer = MatchComputer(host, knownComputers);
            if (computer is null)
            {
                hosts.Add(host);
                continue;
            }

            matched.Add(computer.DistinguishedName);
            hosts.Add(new NetworkHostInfo
            {
                IpAddress = host.IpAddress,
                HostName = host.HostName ?? computer.Name,
                Online = true,
                Origin = NetworkHostOrigin.Both,
                AdDistinguishedName = computer.DistinguishedName,
                AdOuDistinguishedName = computer.OuDistinguishedName,
                OperatingSystem = computer.OperatingSystem,
                CheckedAtUtc = checkedAt,
            });
        }

        // Résolution directe des ordinateurs d'AD DS non encore rapprochés
        // (le DNS inverse peut manquer alors que le DNS direct fonctionne).
        foreach (var computer in knownComputers)
        {
            if (matched.Contains(computer.DistinguishedName)
                || string.IsNullOrWhiteSpace(computer.Name))
            {
                continue;
            }

            try
            {
                var addresses = Dns.GetHostAddresses(computer.Name);
                var address = addresses.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork);
                if (address is null)
                {
                    continue;
                }

                if (hostsByIp.TryGetValue(address.ToString(), out var host))
                {
                    matched.Add(computer.DistinguishedName);
                    hosts.Add(new NetworkHostInfo
                    {
                        IpAddress = host.IpAddress,
                        HostName = host.HostName ?? computer.Name,
                        Online = true,
                        Origin = NetworkHostOrigin.Both,
                        AdDistinguishedName = computer.DistinguishedName,
                        AdOuDistinguishedName = computer.OuDistinguishedName,
                        OperatingSystem = computer.OperatingSystem,
                        CheckedAtUtc = checkedAt,
                    });
                }
            }
            catch (SocketException)
            {
                // Nom non résolvable : l'ordinateur reste « connu d'AD DS, hors ligne ».
            }
        }

        // Ordinateurs d'AD DS non joignables lors du balayage : conservés, jamais supprimés.
        foreach (var computer in knownComputers)
        {
            if (matched.Contains(computer.DistinguishedName))
            {
                continue;
            }

            hosts.Add(new NetworkHostInfo
            {
                IpAddress = string.Empty,
                HostName = computer.Name,
                Online = false,
                Origin = NetworkHostOrigin.AdComputer,
                AdDistinguishedName = computer.DistinguishedName,
                AdOuDistinguishedName = computer.OuDistinguishedName,
                OperatingSystem = computer.OperatingSystem,
                CheckedAtUtc = checkedAt,
            });
        }

        return hosts
            .OrderByDescending(host => host.Online)
            .ThenBy(host => host.HostName ?? host.IpAddress, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Rapproche un hôte découvert d'un objet ordinateur d'AD DS :
    /// par étiquette de nom (PC01 depuis pc01.sarytany.local), insensible à la casse.
    /// </summary>
    private static ComputerInfo? MatchComputer(
        NetworkHostInfo host,
        IReadOnlyList<ComputerInfo> knownComputers)
    {
        if (string.IsNullOrWhiteSpace(host.HostName))
        {
            return null;
        }

        var label = host.HostName.Split('.')[0];
        return knownComputers.FirstOrDefault(computer
            => string.Equals(computer.Name, label, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Résout l'adresse IPv4 du serveur cible : adresse littérale
    /// (connexion par IP) ou résolution DNS locale du FQDN.
    /// </summary>
    private async Task<string> ResolveServerAddressAsync(
        DomainContext context,
        CancellationToken cancellationToken)
    {
        var server = context.ServerFqdn;

        // Connexion par adresse IP littérale : le masque réseau est déduit directement.
        if (DomainValidator.IsIPv4Address(server))
        {
            return server.Trim();
        }

        var command = new SimplePowerShellCommand(
            "DnsResolution",
            "Resolve-DnsName -Name '" + DomainValidator.NormalizeFqdn(server) + "' -Type A -ErrorAction Stop | Select-Object -First 1 Name, IPAddress",
            TimeSpan.FromSeconds(20));

        var result = await _executors.Local.ExecuteAsync(command, cancellationToken);
        if (result.ExitCode == 0 && !result.TimedOut)
        {
            var element = _parser.ParseRaw(result.StandardOutput);
            var ipAddress = ReadString(element, "IPAddress");
            if (!string.IsNullOrWhiteSpace(ipAddress) && DomainValidator.IsIPv4Address(ipAddress))
            {
                return ipAddress;
            }
        }

        throw new InvalidOperationException(
            "Impossible de résoudre l'adresse IPv4 du serveur « " + server
            + " » : la découverte du masque réseau nécessite une adresse IPv4 (FQDN résolvable ou adresse littérale).");
    }

    /// <summary>
    /// Détermine le masque de sous-réseau associé à l'adresse IPv4 fournie en
    /// interrogeant les interfaces réseau locales. Si le masque ne peut être
    /// déduit, on retombe sur le masque par défaut (/24) pour maintenir la
    /// compatibilité avec les anciens comportements.
    /// </summary>
    private static int GetSubnetMask(string ipv4Address)
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                var ipProps = ni.GetIPProperties();
                foreach (var uni in ipProps.UnicastAddresses)
                {
                    if (uni.Address.AddressFamily != AddressFamily.InterNetwork)
                    {
                        continue;
                    }

                    if (!string.Equals(uni.Address.ToString(), ipv4Address, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    // PrefixLength est directement la notation CIDR de l'interface.
                    if (uni.PrefixLength >= 0 && uni.PrefixLength <= 32)
                    {
                        return uni.PrefixLength;
                    }
                }
            }
        }
        catch
        {
            // En cas d'erreur d'accès au réseau, on utilise le masque par défaut.
        }

        // Valeur de repli : masque /24
        return DefaultSubnetMask;
    }

    /// <summary>
    /// Nombre d'hôtes balayables dans le masque fourni (deux adresses réservées :
    /// réseau et diffusion). Les masques /31 et /32 ne comportent pas de plage
    /// d'hôtes classique : le balayage est alors sans objet.
    /// </summary>
    private static int GetHostCountFromSubnetMask(int subnetMask)
    {
        if (subnetMask < 0 || subnetMask > 32)
        {
            subnetMask = DefaultSubnetMask;
        }

        if (subnetMask >= 31)
        {
            return 0;
        }

        var hostBits = 32 - subnetMask;
        var count = (1 << hostBits) - 2;
        return count < 0 ? 0 : count;
    }

    /// <summary>
    /// Obtient le préfixe de réseau (premiers octets) en fonction du masque de sous-réseau réel.
    /// Contrairement à l'ancienne méthode qui supposait toujours /24, cette méthode
    /// adapte la plage d'adresses IP au masque réel configuré sur le système.
    /// </summary>
    private static string GetNetworkPrefix(string ipv4Address, int subnetMask)
    {
        // Valider le masque (doit être entre 0 et 32)
        if (subnetMask < 0 || subnetMask > 32)
        {
            subnetMask = DefaultSubnetMask;
        }

        var fullOctets = subnetMask / 8;     // Octets entiers de réseau
        var remainingBits = subnetMask % 8;  // Bits restants dans le dernier octet

        var octets = ipv4Address.Split('.');
        if (octets.Length != 4)
        {
            return ipv4Address;
        }

        var prefix = new StringBuilder();

        // Octets entiers de réseau.
        for (int i = 0; i < fullOctets; i++)
        {
            prefix.Append(octets[i]);
            prefix.Append('.');
        }

        // Bits partiels dans l'octet suivant : on applique le masque de bits.
        if (remainingBits > 0 && fullOctets < 4)
        {
            var remainingMask = (1 << (8 - remainingBits)) - 1;
            if (int.TryParse(octets[fullOctets], out var lastOctetValue))
            {
                var networkLastOctet = lastOctetValue & ~remainingMask;
                prefix.Append(networkLastOctet);
                prefix.Append('.');
            }
        }

        // Retourne le préfixe réseau terminé par un point, prêt à recevoir
        // le dernier octet de l'hôte (ex. « 192.168.1. »).
        return prefix.ToString();
    }

    /// <summary>
    /// Éléments découverts par le balayage. Délègue à
    /// <see cref="PowerShellOutputParser"/> : la sortie du balayage distant peut
    /// être mêlée à du bruit (runspaces, CLIXML) et ne doit pas faire échouer
    /// la découverte.
    /// </summary>
    private static IEnumerable<JsonElement> EnumerateDiscoveredElements(string json)
        => PowerShellOutputParser.EnumerateArray(json);

    private static string? ReadString(JsonElement? element, string propertyName)
    {
        if (element is null)
        {
            return null;
        }

        return element.Value.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
