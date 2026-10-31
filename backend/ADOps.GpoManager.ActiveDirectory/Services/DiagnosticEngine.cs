using System.Diagnostics;
using System.Text.Json;
using ADOps.GpoManager.Core.Commands;
using ADOps.GpoManager.Core.Diagnostics;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Services;
using ADOps.GpoManager.PowerShell;
using ADOps.GpoManager.PowerShell.Host;

namespace ADOps.GpoManager.ActiveDirectory;

/// <summary>
/// Moteur de diagnostic intelligent — exécute les contrôles en séquence logique,
/// isole les causes racines et produit un <see cref="DiagnosticReport"/> standardisé.
/// Le moteur ne modifie jamais le système : la remédiation relève de
/// <see cref="IRemediationService"/>, sur confirmation explicite.
/// Principe « fail-safe » : un contrôle n'est jamais exécuté par le mécanisme
/// qu'il teste (WinRM est sondé localement, jamais via WinRM).
/// </summary>
public sealed class DiagnosticEngine : IDiagnosticEngine
{
    private readonly IPowerShellExecutorFactory _executors;
    private readonly IErrorInterpreter _interpreter;
    private readonly IAuditService _audit;
    private readonly ILogger _logger;

    public DiagnosticEngine(
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

    /// <summary>Identifiants stables des contrôles (réutilisés par la remédiation).</summary>
    public static class CheckIds
    {
        public const string NetworkConnectivity = "Network.Connectivity";
        public const string DnsResolution = "Dns.Resolution";
        public const string RsatModules = "Rsat.Modules";
        public const string WinRm = "WinRm.Service";
        public const string Authentication = "Authentication.Session";
        public const string GpoAccess = "Gpo.Access";
    }

    public IReadOnlyList<DiagnosticCheckDescriptor> Checks { get; } = new[]
    {
        new DiagnosticCheckDescriptor
        {
            CheckId = CheckIds.NetworkConnectivity,
            Name = "Connectivité réseau du contrôleur",
            Category = DiagnosticCategory.Network,
            Order = 10,
        },
        new DiagnosticCheckDescriptor
        {
            CheckId = CheckIds.DnsResolution,
            Name = "Résolution DNS et enregistrements SRV",
            Category = DiagnosticCategory.Dns,
            Order = 20,
        },
        new DiagnosticCheckDescriptor
        {
            CheckId = CheckIds.RsatModules,
            Name = "Modules PowerShell locaux (RSAT : ActiveDirectory / GroupPolicy)",
            Category = DiagnosticCategory.Rsat,
            Order = 30,
        },
        new DiagnosticCheckDescriptor
        {
            CheckId = CheckIds.WinRm,
            Name = "WinRM distant (ports 5985/5986, écouteur, service, pare-feu)",
            Category = DiagnosticCategory.WinRm,
            Order = 40,
        },
        new DiagnosticCheckDescriptor
        {
            CheckId = CheckIds.Authentication,
            Name = "Authentification (identité et droits sur le contrôleur)",
            Category = DiagnosticCategory.Authentication,
            Order = 50,
            DependsOn = new[] { CheckIds.WinRm },
        },
        new DiagnosticCheckDescriptor
        {
            CheckId = CheckIds.GpoAccess,
            Name = "Accès GroupPolicy (Get-GPO -All, SYSVOL)",
            Category = DiagnosticCategory.GpoAccess,
            Order = 60,
            DependsOn = new[] { CheckIds.WinRm },
        },
    };

    /// <inheritdoc />
    public async Task<DiagnosticReport> RunAllAsync(DomainContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var results = new List<DiagnosticCheckResult>();
        var byId = new Dictionary<string, DiagnosticCheckResult>(StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in Checks.OrderBy(check => check.Order))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Fail-safe : un contrôle dont une dépendance a échoué n'est pas exécuté.
            var blocked = descriptor.DependsOn
                .Select(id => byId.TryGetValue(id, out var dependency) ? dependency : null)
                .FirstOrDefault(dependency => dependency is not null && !dependency.IsOk);
            if (blocked is not null)
            {
                var skipped = new DiagnosticCheckResult
                {
                    CheckId = descriptor.CheckId,
                    Name = descriptor.Name,
                    Category = descriptor.Category,
                    Status = DiagnosticStatus.Skipped,
                    ErrorCode = "DEPENDENCY_FAILED",
                    Details = "Non exécuté : le contrôle « " + blocked.Name + " » a échoué (cause racine probable).",
                    SuggestedFixes = new[] { "Corrigez d'abord le contrôle « " + blocked.Name + " », puis relancez le diagnostic." },
                };
                results.Add(skipped);
                byId[descriptor.CheckId] = skipped;
                continue;
            }

            var result = await RunCheckCoreAsync(context, descriptor.CheckId, cancellationToken).ConfigureAwait(false);
            results.Add(result);
            byId[descriptor.CheckId] = result;
        }

        var rootCauses = IsolateRootCauses(results);
        var targetIp = byId.TryGetValue(CheckIds.DnsResolution, out var dns)
            ? ExtractString(dns.RawOutput, "TargetIp")
            : null;

        return new DiagnosticReport
        {
            TargetController = context.ServerFqdn,
            DetectedDomain = context.DomainFqdn,
            Mode = context.ExecutorMode,
            Results = results,
            PassedCount = results.Count(result => result.Status == DiagnosticStatus.Passed),
            WarningCount = results.Count(result => result.Status == DiagnosticStatus.Warning),
            FailedCount = results.Count(result => result.Status == DiagnosticStatus.Failed),
            SkippedCount = results.Count(result => result.Status == DiagnosticStatus.Skipped),
            TargetIp = targetIp,
            RootCauses = rootCauses,
        };
    }

    /// <inheritdoc />
    public Task<DiagnosticCheckResult> RunCheckAsync(
        DomainContext context,
        string checkId,
        CancellationToken cancellationToken = default)
        => RunCheckCoreAsync(context, checkId, cancellationToken);

    private async Task<DiagnosticCheckResult> RunCheckCoreAsync(
        DomainContext context,
        string checkId,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = checkId switch
            {
                CheckIds.NetworkConnectivity => await RunNetworkConnectivityAsync(context, cancellationToken).ConfigureAwait(false),
                CheckIds.DnsResolution => await RunDnsResolutionAsync(context, cancellationToken).ConfigureAwait(false),
                CheckIds.RsatModules => await RunRsatModulesAsync(context, cancellationToken).ConfigureAwait(false),
                CheckIds.WinRm => await RunWinRmAsync(context, cancellationToken).ConfigureAwait(false),
                CheckIds.Authentication => await RunAuthenticationAsync(context, cancellationToken).ConfigureAwait(false),
                CheckIds.GpoAccess => await RunGpoAccessAsync(context, cancellationToken).ConfigureAwait(false),
                _ => throw new ArgumentOutOfRangeException(nameof(checkId), "Contrôle de diagnostic inconnu : " + checkId),
            };

            result.Duration = stopwatch.Elapsed;

            await _audit.LogAsync(
                AuditActions.DiagnosticRun,
                context.ServerFqdn,
                result.Status == DiagnosticStatus.Failed ? AuditResult.Failure : AuditResult.Success,
                "Diagnostic « " + result.Name + " » : " + result.Status + (result.ErrorCode is null ? string.Empty : " (" + result.ErrorCode + ")"),
                result.Details,
                new AuditContext(
                    Server: context.ServerFqdn,
                    Domain: context.DomainFqdn,
                    ErrorCode: result.ErrorCode,
                    Duration: result.Duration,
                    OperationId: Guid.NewGuid().ToString("N"))).ConfigureAwait(false);

            _logger.Information("Diagnostic " + result.CheckId + " : " + result.Status
                + " (" + result.Duration.TotalMilliseconds.ToString("0") + " ms) — " + result.Details);

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.Error("Diagnostic " + checkId + " en erreur", ex);
            return new DiagnosticCheckResult
            {
                CheckId = checkId,
                Name = Checks.First(descriptor => descriptor.CheckId == checkId).Name,
                Status = DiagnosticStatus.Failed,
                ErrorCode = "CHECK_EXECUTION_ERROR",
                Details = "Erreur inattendue pendant le contrôle : " + ex.Message,
                SuggestedFixes = new[] { "Consulter le journal %LOCALAPPDATA%\\ADOps\\GpoManager\\logs\\adops.log." },
                Duration = stopwatch.Elapsed,
            };
        }
    }

    /// <summary>Exécuteur adapté au mode : local (RSAT) ou distant (WinRM, credentials transportés par DPAPI).</summary>
    private IPowerShellExecutor ExecutorFor(DomainContext context)
        => context.ExecutorMode == ExecutorMode.Remote
            ? _executors.CreateRemote(context.ServerFqdn, context.Credentials)
            : _executors.Local;

    private async Task<DiagnosticCheckResult> RunNetworkConnectivityAsync(DomainContext context, CancellationToken cancellationToken)
    {
        var command = new DiagnoseDnsCommand(context.ServerFqdn, context.DomainFqdn);
        var element = await ExecuteAndParseAsync(_executors.Local, command, cancellationToken).ConfigureAwait(false);

        var reachable = GetBoolean(element, "TargetReachable");
        var targetIp = GetString(element, "TargetIp");

        if (reachable)
        {
            return Ok(CheckIds.NetworkConnectivity, "Connectivité réseau du contrôleur", DiagnosticCategory.Network,
                "Contrôleur " + context.ServerFqdn + " répond au ping (ICMP)"
                + (targetIp is null ? string.Empty : " — adresse " + targetIp) + ".",
                element);
        }

        return new DiagnosticCheckResult
        {
            CheckId = CheckIds.NetworkConnectivity,
            Name = "Connectivité réseau du contrôleur",
            Category = DiagnosticCategory.Network,
            Status = DiagnosticStatus.Warning,
            ErrorCode = "TARGET_UNREACHABLE",
            Details = "Le contrôleur " + context.ServerFqdn + " ne répond pas au ping (ICMP). "
                + "Cela peut indiquer une machine éteinte, un problème réseau, ou un pare-feu filtrant ICMP — ce n'est pas une preuve d'extinction.",
            SuggestedFixes = new[]
            {
                "Vérifiez l'alimentation et la connectivité du contrôleur.",
                "Vérifiez que le poste et le contrôleur sont sur le même réseau/routage.",
                "Si le ping est filtré par stratégie, poursuivez le diagnostic : les contrôles DNS et WinRM sont indépendants du ping.",
            },
            RawOutput = element is null ? null : element.Value.GetRawText(),
        };
    }

    private async Task<DiagnosticCheckResult> RunDnsResolutionAsync(DomainContext context, CancellationToken cancellationToken)
    {
        var command = new DiagnoseDnsCommand(context.ServerFqdn, context.DomainFqdn);
        var element = await ExecuteAndParseAsync(_executors.Local, command, cancellationToken).ConfigureAwait(false);

        var domainResolved = GetBoolean(element, "DomainResolved");
        var srvCount = GetInt32(element, "SrvRecordCount");
        var srvTargets = GetStringArray(element, "SrvTargets");
        var clientDns = GetStringArray(element, "ClientDnsServers");
        var fixes = new List<string>();

        if (domainResolved && srvCount > 0)
        {
            return Ok(CheckIds.DnsResolution, "Résolution DNS et enregistrements SRV", DiagnosticCategory.Dns,
                "Domaine " + context.DomainFqdn + " résolu. " + srvCount + " enregistrement(s) SRV _ldap._tcp.dc._msdcs trouvé(s) : "
                + string.Join(", ", srvTargets) + ". Serveurs DNS du client : " + (clientDns.Count > 0 ? string.Join(", ", clientDns) : "aucun")
                + ".", element);
        }

        if (!domainResolved)
        {
            fixes.Add("Vérifiez que les serveurs DNS du client pointent vers le contrôleur (ipconfig /all).");
            fixes.Add("Sur le contrôleur, vérifiez que la zone « " + context.DomainFqdn + " » existe et accepte les mises à jour dynamiques.");
            fixes.Add("Videz le cache DNS client et réenregistrez : Clear-DnsClientCache puis ipconfig /registerdns.");
        }

        if (srvCount == 0)
        {
            fixes.Add("Enregistrements SRV manquants (_ldap._tcp.dc._msdcs." + context.DomainFqdn + ") : les stations ne localisent pas les contrôleurs.");
            fixes.Add("Sur le DNS hébergeant la zone _msdcs, forcez l'enregistrement : sur le contrôleur, redémarrez le service Netlogon (Restart-Service Netlogon).");
            fixes.Add("Vérifiez manuellement : Resolve-DnsName -Name _ldap._tcp.dc._msdcs." + context.DomainFqdn + " -Type SRV");
        }

        var errorCode = !domainResolved ? "DOMAIN_UNRESOLVED" : "SRV_RECORD_MISSING";

        return new DiagnosticCheckResult
        {
            CheckId = CheckIds.DnsResolution,
            Name = "Résolution DNS et enregistrements SRV",
            Category = DiagnosticCategory.Dns,
            Status = DiagnosticStatus.Failed,
            ErrorCode = errorCode,
            Details = (!domainResolved
                ? "Le domaine " + context.DomainFqdn + " ne résout pas depuis ce poste."
                : "Aucun enregistrement SRV _ldap._tcp.dc._msdcs." + context.DomainFqdn + " trouvé.")
                + " Serveurs DNS du client : " + (clientDns.Count > 0 ? string.Join(", ", clientDns) : "aucun configuré")
                + ". La correction côté serveur DNS reste une action manuelle (assistant de réparation).",
            SuggestedFixes = fixes,
            RequiresElevation = false,
            CanAutoFix = !domainResolved, // seul le vidage de cache/réenregistrement est automatisable sans risque
            RawOutput = element is null ? null : element.Value.GetRawText(),
        };
    }

    private async Task<DiagnosticCheckResult> RunRsatModulesAsync(DomainContext context, CancellationToken cancellationToken)
    {
        var command = new DiagnoseRsatCommand();
        var element = await ExecuteAndParseAsync(_executors.Local, command, cancellationToken).ConfigureAwait(false);

        var hasAd = GetBoolean(element, "ActiveDirectoryModule");
        var hasGpo = GetBoolean(element, "GroupPolicyModule");
        var osCaption = GetString(element, "OsCaption") ?? "système inconnu";
        var isAdmin = GetBoolean(element, "IsAdministrator");

        if (hasAd && hasGpo)
        {
            return Ok(CheckIds.RsatModules, "Modules PowerShell locaux (RSAT : ActiveDirectory / GroupPolicy)", DiagnosticCategory.Rsat,
                "Modules ActiveDirectory (" + (GetString(element, "ActiveDirectoryVersion") ?? "?") + ") et GroupPolicy ("
                + (GetString(element, "GroupPolicyVersion") ?? "?") + ") disponibles sur " + osCaption + "."
                + (isAdmin ? string.Empty : " Session sans élévation (certaines réparations nécessiteront les droits administrateur)."),
                element);
        }

        var missing = new List<string>();
        if (!hasAd)
        {
            missing.Add("ActiveDirectory");
        }

        if (!hasGpo)
        {
            missing.Add("GroupPolicy");
        }

        return new DiagnosticCheckResult
        {
            CheckId = CheckIds.RsatModules,
            Name = "Modules PowerShell locaux (RSAT : ActiveDirectory / GroupPolicy)",
            Category = DiagnosticCategory.Rsat,
            Status = DiagnosticStatus.Failed,
            ErrorCode = "RSAT_MODULE_MISSING",
            Details = "Module(s) RSAT absent(s) de ce poste (" + osCaption + ") : " + string.Join(", ", missing)
                + ". Les commandes AD DS / GPO ne peuvent pas s'exécuter en local."
                + (isAdmin ? string.Empty : " La session n'est pas élevée : l'installation des fonctionnalités échouera sans élévation."),
            SuggestedFixes = new[]
            {
                "Installez les fonctionnalités : Add-WindowsCapability -Online -Name Rsat.ActiveDirectory.DS-LDS.Tools~~~~0.0.1.0 (et Rsat.GroupPolicy.Management.Tools~~~~0.0.1.0).",
                "Alternative sans installation : utilisez le mode distant (WinRM) — les commandes s'exécutent alors sur le contrôleur.",
            },
            RequiresElevation = true,
            CanAutoFix = true,
            RawOutput = element is null ? null : element.Value.GetRawText(),
        };
    }

    private async Task<DiagnosticCheckResult> RunWinRmAsync(DomainContext context, CancellationToken cancellationToken)
    {
        // Sondage LOCAL : WinRM n'est jamais testé à travers WinRM (principe fail-safe).
        var command = new DiagnoseWinRmCommand(context.ServerFqdn);
        var element = await ExecuteAndParseAsync(_executors.Local, command, cancellationToken).ConfigureAwait(false);

        var port5985 = GetBoolean(element, "HttpPort5985Open");
        var port5986 = GetBoolean(element, "HttpsPort5986Open");
        var responds = GetBoolean(element, "WsManResponds");
        var serviceState = GetString(element, "LocalServiceState") ?? "Inconnu";
        var firewallRules = GetInt32(element, "LocalFirewallRuleCount");

        if (responds && (port5985 || port5986))
        {
            return Ok(CheckIds.WinRm, "WinRM distant (ports 5985/5986, écouteur, service, pare-feu)", DiagnosticCategory.WinRm,
                "Écouteur WinRM opérationnel sur " + context.ServerFqdn + " (5985 " + (port5985 ? "ouvert" : "fermé")
                + ", 5986 " + (port5986 ? "ouvert" : "fermé") + "). Service local : " + serviceState + ".",
                element);
        }

        var details = "WinRM inaccessible sur " + context.ServerFqdn + " : port 5985 " + (port5985 ? "ouvert" : "fermé")
            + ", port 5986 " + (port5986 ? "ouvert" : "fermé") + ", écouteur " + (responds ? "répond" : "muet") + "."
            + " Périmètre local uniquement : service WinRM « " + serviceState + " », " + firewallRules + " règle(s) de pare-feu du groupe « Windows Remote Management » sur ce poste.";

        return new DiagnosticCheckResult
        {
            CheckId = CheckIds.WinRm,
            Name = "WinRM distant (ports 5985/5986, écouteur, service, pare-feu)",
            Category = DiagnosticCategory.WinRm,
            Status = DiagnosticStatus.Failed,
            ErrorCode = port5985 || port5986 ? "WINRM_LISTENER_NOT_RESPONDING" : "WINRM_PORT_CLOSED",
            Details = details,
            SuggestedFixes = new[]
            {
                "Sur le contrôleur (ou ce poste en local), exécutez en administrateur : Enable-PSRemoting -Force.",
                "Vérifiez les règles de pare-feu entrantes du groupe « Windows Remote Management » (ports 5985/5986).",
                "L'assistant peut appliquer ces corrections localement (droits administrateur requis). Aucune option non sécurisée (TrustedHosts) n'est activée automatiquement.",
                "Sans WinRM, utilisez le mode local avec les modules RSAT installés.",
            },
            RequiresElevation = true,
            CanAutoFix = true,
            RawOutput = element is null ? null : element.Value.GetRawText(),
        };
    }

    private async Task<DiagnosticCheckResult> RunAuthenticationAsync(DomainContext context, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Compte Windows actuel : l'identité locale est vérifiée sans WinRM.
            // Compte explicite : la session est validée sur le contrôleur (credentials
            // transportés par le blob DPAPI de l'exécuteur distant).
            var command = new DiagnoseAuthenticationCommand(context.ServerFqdn);
            var element = await ExecuteAndParseAsync(ExecutorFor(context), command, cancellationToken).ConfigureAwait(false);

            var userName = GetString(element, "UserName") ?? "(inconnu)";
            var domain = GetString(element, "Domain") ?? context.DomainFqdn;
            var isAdmin = GetBoolean(element, "IsAdministrator");

            return Ok(CheckIds.Authentication, "Authentification (identité et droits sur le contrôleur)", DiagnosticCategory.Authentication,
                "Session authentifiée sur " + context.ServerFqdn + " : " + domain + "\\" + userName
                + (isAdmin ? " (droits administrateur)." : " (session non élevée : certaines opérations GPO peuvent être refusées)."),
                element);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.Warning("Diagnostic authentification en échec : " + ex.Message);

            // Distinction réseau / identification / droits — sans jamais journaliser de secret.
            var message = ex.ToString();
            string errorCode;
            var isNetwork = message.Contains("WinRM", StringComparison.OrdinalIgnoreCase)
                || message.Contains("RPC", StringComparison.OrdinalIgnoreCase)
                || message.Contains("temps", StringComparison.OrdinalIgnoreCase);
            string classification;
            if (isNetwork)
            {
                errorCode = "AUTH_NETWORK_ERROR";
                classification = "erreur réseau / transport (le service distant n'a pas pu être joint)";
            }
            else if (message.Contains("accès refusé", StringComparison.OrdinalIgnoreCase)
                     || message.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
                     || message.Contains("0x8007052E", StringComparison.OrdinalIgnoreCase)
                     || message.Contains("nom d'utilisateur ou mot de passe", StringComparison.OrdinalIgnoreCase))
            {
                errorCode = message.Contains("0x80070005", StringComparison.OrdinalIgnoreCase) ? "AUTH_ACCESS_DENIED" : "AUTH_BAD_CREDENTIALS";
                classification = "authentification refusée (identifiant ou mot de passe incorrect, ou compte verrouillé)";
            }
            else
            {
                errorCode = "AUTH_UNKNOWN_ERROR";
                classification = "erreur non classifiée";
            }

            return new DiagnosticCheckResult
            {
                CheckId = CheckIds.Authentication,
                Name = "Authentification (identité et droits sur le contrôleur)",
                Category = DiagnosticCategory.Authentication,
                Status = DiagnosticStatus.Failed,
                ErrorCode = errorCode,
                Details = "Échec d'authentification sur " + context.ServerFqdn + " — " + classification + ".",
                SuggestedFixes = new[]
                {
                    "Vérifiez le format du compte : DOMAINE\\utilisateur ou utilisateur@domaine (UPN).",
                    "Vérifiez que le mot de passe est correct et que le compte n'est pas verrouillé.",
                    "Vérifiez les droits délégués dans AD DS (l'ouverture de session interactive/distante peut être restreinte).",
                    "Cause système possible : " + ex.Message,
                },
                Duration = stopwatch.Elapsed,
            };
        }
    }

    private async Task<DiagnosticCheckResult> RunGpoAccessAsync(DomainContext context, CancellationToken cancellationToken)
    {
        var command = new DiagnoseGpoAccessCommand(context.DomainFqdn);

        try
        {
            var element = await ExecuteAndParseAsync(ExecutorFor(context), command, cancellationToken).ConfigureAwait(false);

            var gpoCount = GetInt32(element, "GpoCount");
            var sysvol = GetBoolean(element, "SysvolReachable");
            var domainDn = GetString(element, "DomainDn");

            if (sysvol && gpoCount >= 0)
            {
                return Ok(CheckIds.GpoAccess, "Accès GroupPolicy (Get-GPO -All, SYSVOL)", DiagnosticCategory.GpoAccess,
                    gpoCount + " GPO lue(s) dans " + context.DomainFqdn
                    + (domainDn is null ? string.Empty : " (" + domainDn + ")") + ", partage SYSVOL accessible.",
                    element);
            }

            return new DiagnosticCheckResult
            {
                CheckId = CheckIds.GpoAccess,
                Name = "Accès GroupPolicy (Get-GPO -All, SYSVOL)",
                Category = DiagnosticCategory.GpoAccess,
                Status = DiagnosticStatus.Failed,
                ErrorCode = "GPO_ACCESS_FAILED",
                Details = "Lecture des GPO possible (" + gpoCount + ") mais le partage SYSVOL \\\\" + context.DomainFqdn + "\\SYSVOL est inaccessible.",
                SuggestedFixes = new[]
                {
                    "Vérifiez la résolution DNS du domaine (prérequis au chemin UNC SYSVOL).",
                    "Vérifiez que le partage SYSVOL existe sur le contrôleur et que le compte dispose des droits de lecture.",
                    "Vérifiez les autorisations NTFS sur <chemin>\\SYSVOL\\<domaine>\\Policies.",
                },
                RawOutput = element is null ? null : element.Value.GetRawText(),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warning("Diagnostic accès GPO en échec : " + ex.Message);
            return new DiagnosticCheckResult
            {
                CheckId = CheckIds.GpoAccess,
                Name = "Accès GroupPolicy (Get-GPO -All, SYSVOL)",
                Category = DiagnosticCategory.GpoAccess,
                Status = DiagnosticStatus.Failed,
                ErrorCode = "GPO_ACCESS_ERROR",
                Details = "L'accès GroupPolicy a échoué : " + ex.Message + ".",
                SuggestedFixes = new[]
                {
                    "Vérifiez les modules RSAT locaux (contrôle « Modules PowerShell locaux »).",
                    "Vérifiez la connectivité WinRM (contrôle « WinRM distant »).",
                    "Vérifiez les droits de lecture sur AD DS et SYSVOL.",
                },
                RawOutput = null,
            };
        }
    }

    private async Task<JsonElement?> ExecuteAndParseAsync(
        IPowerShellExecutor executor,
        PowerShellCommand command,
        CancellationToken cancellationToken)
    {
        var result = await executor.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0 || result.TimedOut || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            var error = _interpreter.Interpret(result, command.Name);
            throw new InvalidOperationException(
                error.Title + " — " + error.Cause
                + (string.IsNullOrWhiteSpace(result.StandardError) ? string.Empty : " | " + result.StandardError.Trim()));
        }

        // La sortie peut être mêlée à du bruit (CLIXML, bannières) : l'isolement du
        // JSON et l'absorption d'une sortie illisible sont gérés par le parseur,
        // pour qu'un contrôle de diagnostic ne s'effondre pas sur du bruit.
        if (!PowerShellOutputParser.TryParseObject(result.StandardOutput, out var root))
        {
            throw new InvalidOperationException(
                "La commande « " + command.Name + " » s'est exécutée sans erreur mais sa sortie "
                + "n'a pas pu être analysée. Sortie brute : "
                + (string.IsNullOrWhiteSpace(result.StandardOutput) ? "(vide)" : result.StandardOutput.Trim()));
        }

        return root;
    }

    private static DiagnosticCheckResult Ok(
        string checkId,
        string name,
        DiagnosticCategory category,
        string details,
        JsonElement? element)
        => new()
        {
            CheckId = checkId,
            Name = name,
            Category = category,
            Status = DiagnosticStatus.Passed,
            Details = details,
            RawOutput = element is null ? null : element.Value.GetRawText(),
        };

    private static IReadOnlyList<string> IsolateRootCauses(IReadOnlyList<DiagnosticCheckResult> results)
    {
        var causes = new List<string>();

        foreach (var failure in results.Where(result => result.Status == DiagnosticStatus.Failed))
        {
            causes.Add(failure.CheckId switch
            {
                CheckIds.NetworkConnectivity => "Cause racine réseau : le contrôleur ne répond pas au ping (machine éteinte, routage, ou ICMP filtré).",
                CheckIds.DnsResolution => "Cause racine DNS : la résolution du domaine ou les enregistrements SRV _msdcs sont absents — les stations ne localisent pas les contrôleurs.",
                CheckIds.WinRm => "Cause racine WinRM : ports 5985/5986 fermés ou écouteur muet — les contrôles distants (authentification, GPO) sont bloqués en cascade.",
                CheckIds.Authentication => "Cause racine authentification : identification ou droits insuffisants sur le contrôleur.",
                CheckIds.GpoAccess => "Cause racine GroupPolicy : accès SYSVOL ou lecture AD DS refusée.",
                CheckIds.RsatModules => "Cause racine RSAT : modules locaux absents — le mode local est impossible.",
                _ => "Échec du contrôle « " + failure.Name + " » (" + (failure.ErrorCode ?? "sans code") + ").",
            });
        }

        foreach (var skipped in results.Where(result => result.Status == DiagnosticStatus.Skipped))
        {
            causes.Add("Contrôle « " + skipped.Name + " » non exécuté : dépendance en échec (voir causes ci-dessus).");
        }

        return causes;
    }

    // --- Lecture tolérante du JSON de diagnostic (jamais d'exception sur champ absent) ---

    private static string? ExtractString(string? rawJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        if (!PowerShellOutputParser.TryParseObject(rawJson, out var root))
        {
            return null;
        }

        return GetString(root, propertyName);
    }

    private static string? GetString(JsonElement? element, string propertyName)
    {
        if (element is null)
        {
            return null;
        }

        return element.Value.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool GetBoolean(JsonElement? element, string propertyName)
        => element is not null
           && element.Value.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.True;

    private static int GetInt32(JsonElement? element, string propertyName)
    {
        if (element is null || !element.Value.TryGetProperty(propertyName, out var value))
        {
            return 0;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetInt32(),
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => 0,
        };
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement? element, string propertyName)
    {
        if (element is null || !element.Value.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var items = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                items.Add(item.GetString() ?? string.Empty);
            }
        }

        return items;
    }
}
