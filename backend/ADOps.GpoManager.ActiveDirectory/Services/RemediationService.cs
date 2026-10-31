using ADOps.GpoManager.Core.Commands;
using ADOps.GpoManager.Core.Diagnostics;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Services;
using ADOps.GpoManager.PowerShell.Host;

namespace ADOps.GpoManager.ActiveDirectory;

/// <summary>
/// Assistant de réparation automatisée.
/// Règles de sécurité (cahier des charges) :
/// - aucune action sans confirmation explicite (<paramref name="confirmed"/>) ;
/// - mode diagnostic (DryRun) : aucune modification, journalisation de l'intention ;
/// - les actions à risque sont signalées (élévation, périmètre) dans l'aperçu ;
/// - chaque action est journalisée (jamais de secret) et suivie d'une post-vérification ;
/// - les corrections WinRM sont exécutées en LOCAL (fail-safe) ;
/// - aucune configuration non sécurisée n'est appliquée automatiquement (TrustedHosts).
/// </summary>
public sealed class RemediationService : IRemediationService
{
    private readonly IDiagnosticEngine _engine;
    private readonly IPowerShellExecutorFactory _executors;
    private readonly IAuditService _audit;
    private readonly ILogger _logger;

    public RemediationService(
        IDiagnosticEngine engine,
        IPowerShellExecutorFactory executors,
        IAuditService audit,
        ILogger logger)
    {
        _engine = engine;
        _executors = executors;
        _audit = audit;
        _logger = logger;
    }

    /// <summary>
    /// Catalogue des actions de remédiation connues, indexé par contrôle cible.
    /// Aucune saisie libre : chaque action correspond à une commande typée.
    /// </summary>
    private static IReadOnlyDictionary<string, RemediationAction> ActionsByCheck { get; } =
        new Dictionary<string, RemediationAction>(StringComparer.OrdinalIgnoreCase)
        {
            [DiagnosticEngine.CheckIds.WinRm] = new RemediationAction
            {
                Id = "FixWinRm",
                Title = "Activer WinRM et le pare-feu (exécution locale)",
                Description = "Enable-PSRemoting, démarrage automatique du service, activation des règles de pare-feu 5985/5986. "
                    + "S'exécute sur ce poste en administrateur ; à réitérer sur le contrôleur distant si nécessaire.",
                Risk = RemediationRisk.Elevated,
                RequiresElevation = true,
                CanAutoFix = true,
                CommandName = nameof(EnableWinRmCommand),
                TargetCheckId = DiagnosticEngine.CheckIds.WinRm,
            },
            [DiagnosticEngine.CheckIds.RsatModules] = new RemediationAction
            {
                Id = "InstallRsat",
                Title = "Installer les modules RSAT (ActiveDirectory + GroupPolicy)",
                Description = "Installation des fonctionnalités Windows Rsat.ActiveDirectory.DS-LDS.Tools et "
                    + "Rsat.GroupPolicy.Management.Tools. Nécessite une session administrateur et un accès à Windows Update / sources WSUS.",
                Risk = RemediationRisk.Elevated,
                RequiresElevation = true,
                CanAutoFix = true,
                CommandName = nameof(InstallRsatCommand),
                TargetCheckId = DiagnosticEngine.CheckIds.RsatModules,
            },
            [DiagnosticEngine.CheckIds.DnsResolution] = new RemediationAction
            {
                Id = "ClearDnsCache",
                Title = "Vider le cache DNS client et réenregistrer",
                Description = "Clear-DnsClientCache puis ipconfig /registerdns. Action sans risque ; ne corrige pas une absence "
                    + "d'enregistrement SRV côté serveur DNS (intervention manuelle sur la zone _msdcs).",
                Risk = RemediationRisk.Safe,
                RequiresElevation = false,
                CanAutoFix = true,
                CommandName = nameof(ClearDnsCacheCommand),
                TargetCheckId = DiagnosticEngine.CheckIds.DnsResolution,
            },
        };

    /// <inheritdoc />
    public RemediationPlan BuildPlan(DiagnosticReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var items = new List<RemediationPlanItem>();

        foreach (var result in report.Results.Where(result => result.Status == DiagnosticStatus.Failed))
        {
            if (ActionsByCheck.TryGetValue(result.CheckId, out var action))
            {
                // Cas particulier : contrôle DNS en échec sur les SRV uniquement —
                // le vidage de cache ne corrige rien côté serveur, la correction reste manuelle.
                if (string.Equals(result.CheckId, DiagnosticEngine.CheckIds.DnsResolution, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(result.ErrorCode, "SRV_RECORD_MISSING", StringComparison.OrdinalIgnoreCase))
                {
                    items.Add(new RemediationPlanItem
                    {
                        Action = new RemediationAction
                        {
                            Id = "ManualSrvFix",
                            Title = "Corriger les enregistrements SRV (manuel)",
                            Description = "Sur le serveur DNS hébergeant la zone _msdcs : redémarrez Netlogon sur le contrôleur "
                                + "(Restart-Service Netlogon), vérifiez la zone _ldap._tcp.dc._msdcs." + report.DetectedDomain
                                + " puis validez avec Resolve-DnsName -Type SRV.",
                            Risk = RemediationRisk.ManualOnly,
                            RequiresElevation = true,
                            CanAutoFix = false,
                            TargetCheckId = DiagnosticEngine.CheckIds.DnsResolution,
                        },
                        CheckName = result.Name,
                        IsSelected = false,
                    });
                    continue;
                }

                items.Add(new RemediationPlanItem { Action = action, CheckName = result.Name });
                continue;
            }

            // Échec sans action automatisée : affiché à titre informatif (manuel uniquement).
            items.Add(new RemediationPlanItem
            {
                Action = new RemediationAction
                {
                    Id = "Manual_" + result.CheckId.Replace('.', '_'),
                    Title = "Correction manuelle — " + result.Name,
                    Description = string.Join(" ", result.SuggestedFixes),
                    Risk = RemediationRisk.ManualOnly,
                    RequiresElevation = result.RequiresElevation,
                    CanAutoFix = false,
                    TargetCheckId = result.CheckId,
                },
                CheckName = result.Name,
                IsSelected = false,
            });
        }

        return new RemediationPlan { Items = items };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RemediationExecutionResult>> ExecuteAsync(
        DomainContext context,
        RemediationPlan plan,
        bool confirmed,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(plan);

        if (!confirmed)
        {
            _logger.Warning("Remédiation refusée : confirmation explicite absente.");
            return Array.Empty<RemediationExecutionResult>();
        }

        var results = new List<RemediationExecutionResult>();

        foreach (var item in plan.Items.Where(item => item.IsSelected))
        {
            var action = item.Action;
            cancellationToken.ThrowIfCancellationRequested();

            if (!action.CanAutoFix)
            {
                results.Add(new RemediationExecutionResult
                {
                    Action = action,
                    IsSuccess = false,
                    Message = "Action manuelle : non appliquée par l'application (voir la description).",
                });
                continue;
            }

            if (dryRun)
            {
                _logger.Information("DRY-RUN remédiation « " + action.Title + " » : aucune modification effectuée (commande prévue : " + action.CommandName + ").");
                await _audit.LogAsync(
                    AuditActions.RemediationApply,
                    context.ServerFqdn,
                    AuditResult.Information,
                    "Dry-Run : « " + action.Title + " » simulée, aucune modification.",
                    "Commande typée prévue : " + action.CommandName,
                    new AuditContext(
                        Server: context.ServerFqdn,
                        Domain: context.DomainFqdn,
                        RequestedState: action.Title,
                        OperationId: Guid.NewGuid().ToString("N"))).ConfigureAwait(false);

                results.Add(new RemediationExecutionResult { Action = action, IsSuccess = true, WasDryRun = true, Message = "Simulation (dry-run) : aucune modification." });
                continue;
            }

            results.Add(await ExecuteSingleAsync(context, action, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    /// <summary>
    /// Applique UNE action de remédiation puis rejoue le contrôle cible (post-vérification).
    /// Les commandes de remédiation s'exécutent toujours en LOCAL (fail-safe WinRM).
    /// </summary>
    private async Task<RemediationExecutionResult> ExecuteSingleAsync(
        DomainContext context,
        RemediationAction action,
        CancellationToken cancellationToken)
    {
        var executor = _executors.Local;
        var command = CommandFor(action.CommandName);

        _logger.Information("Remédiation « " + action.Title + " » : exécution locale de " + action.CommandName + ".");

        try
        {
            var result = await executor.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);

            var isSuccess = result.ExitCode == 0 && !result.TimedOut;
            await _audit.LogAsync(
                AuditActions.RemediationApply,
                context.ServerFqdn,
                isSuccess ? AuditResult.Success : AuditResult.Failure,
                (isSuccess ? "Remédiation appliquée : " : "Échec de la remédiation : ") + action.Title,
                "Commande : " + action.CommandName + " — durée " + result.Duration.TotalSeconds.ToString("0.0") + " s"
                + (isSuccess ? string.Empty : " — " + result.StandardError.Trim()),
                new AuditContext(
                    Server: context.ServerFqdn,
                    Domain: context.DomainFqdn,
                    RequestedState: action.Title,
                    ErrorCode: isSuccess ? null : "ExitCode=" + result.ExitCode,
                    Duration: result.Duration,
                    OperationId: Guid.NewGuid().ToString("N"))).ConfigureAwait(false);

            // Post-vérification : le contrôle cible est rejoué pour valider la correction.
            DiagnosticStatus? postCheck = null;
            if (isSuccess && !string.IsNullOrWhiteSpace(action.TargetCheckId))
            {
                try
                {
                    var recheck = await _engine.RunCheckAsync(context, action.TargetCheckId, cancellationToken).ConfigureAwait(false);
                    postCheck = recheck.Status;
                    _logger.Information("Post-vérification " + action.TargetCheckId + " : " + recheck.Status + ".");
                }
                catch (Exception ex)
                {
                    _logger.Warning("Post-vérification " + action.TargetCheckId + " impossible : " + ex.Message);
                }
            }

            return new RemediationExecutionResult
            {
                Action = action,
                IsSuccess = isSuccess && (postCheck is null or DiagnosticStatus.Passed or DiagnosticStatus.Warning),
                Message = isSuccess
                    ? "Appliquée : " + action.Title + "."
                    + (postCheck is null ? string.Empty : " Post-vérification : " + postCheck + ".")
                    : "Échec de « " + action.Title + " ».",
                Error = isSuccess ? null : result.StandardError.Trim(),
                PostCheckStatus = postCheck,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Error("Remédiation « " + action.Title + " » en erreur", ex);
            return new RemediationExecutionResult
            {
                Action = action,
                IsSuccess = false,
                Message = "Erreur inattendue : " + ex.Message,
                Error = ex.ToString(),
            };
        }
    }

    /// <inheritdoc />
    public async Task<DiagnosticReport> RetestAsync(
        DomainContext context,
        IReadOnlyList<string> checkIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (checkIds is null || checkIds.Count == 0)
        {
            return await _engine.RunAllAsync(context, cancellationToken).ConfigureAwait(false);
        }

        var results = new List<DiagnosticCheckResult>();
        foreach (var checkId in checkIds)
        {
            results.Add(await _engine.RunCheckAsync(context, checkId, cancellationToken).ConfigureAwait(false));
        }

        var rootCauses = results
            .Where(result => result.Status == DiagnosticStatus.Failed)
            .Select(result => result.Details)
            .ToList();

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
            RootCauses = rootCauses,
        };
    }

    /// <summary>Résout une commande de remédiation par son nom (jamais de script arbitraire).</summary>
    private static PowerShellCommand CommandFor(string commandName) => commandName switch
    {
        nameof(EnableWinRmCommand) => new EnableWinRmCommand(),
        nameof(InstallRsatCommand) => new InstallRsatCommand(),
        nameof(ClearDnsCacheCommand) => new ClearDnsCacheCommand(),
        _ => throw new ArgumentOutOfRangeException(nameof(commandName), "Commande de remédiation inconnue : " + commandName),
    };
}
