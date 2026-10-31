using System.Diagnostics;
using System.Text.Json;
using ADOps.GpoManager.Core.Commands;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Services;
using ADOps.GpoManager.Core.Security;
using ADOps.GpoManager.PowerShell.Host;

namespace ADOps.GpoManager.PowerShell.Services;

/// <summary>
/// Health Check / diagnostics de connexion (cahier des charges §D) :
/// DNS, modules RSAT locaux, WinRM, authentification, accès AD DS, capacité GroupPolicy.
/// Détermine le mode d'exécution recommandé (local RSAT ou distant WinRM).
/// L'authentification explicite (compte de domaine) est utilisée lorsque fournie.
/// </summary>
public sealed class HealthCheckService : IHealthCheckService, IAuthenticationService
{
    private readonly IPowerShellExecutorFactory _executors;
    private readonly PowerShellOutputParser _parser;
    private readonly IErrorInterpreter _interpreter;
    private readonly ILogger _logger;

    public HealthCheckService(IPowerShellExecutorFactory executors, PowerShellOutputParser parser, IErrorInterpreter interpreter, ILogger logger)
    {
        _executors = executors;
        _parser = parser;
        _interpreter = interpreter;
        _logger = logger;
    }

    public Task<HealthCheckReport> RunAsync(string serverFqdn, string domainFqdn, CancellationToken cancellationToken = default)
        => RunAsync(serverFqdn, domainFqdn, null, null, cancellationToken);

    /// <inheritdoc />
    public Task<HealthCheckReport> RunDiagnosticsAsync(
        string serverFqdn,
        string domainFqdn,
        ConnectionCredentials? credentials = null,
        IProgress<HealthCheckStep>? progress = null,
        CancellationToken cancellationToken = default)
        => RunAsync(serverFqdn, domainFqdn, credentials, progress, cancellationToken);

    public async Task<HealthCheckReport> RunAsync(
        string serverFqdn,
        string domainFqdn,
        ConnectionCredentials? credentials,
        IProgress<HealthCheckStep>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Le test WinRM doit suivre le même canal que l'exécuteur distant
        // (Auto : HTTPS 5986 puis repli HTTP 5985).
        var transport = _executors is PowerShellExecutorFactory factory ? factory.Transport : WinRmTransportMode.Auto;
        var command = new TestConnectionCommand(serverFqdn, transport);
        var remote = _executors.CreateRemote(command.ServerFqdn, credentials);
        var steps = new List<HealthCheckStep>();
        var modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // --- Fonctions de traitement des réussites (captures du contexte du test) ---

        HealthCheckStep OnDnsSuccess(PowerShellExecutionResult result, TimeSpan duration)
        {
            var element = _parser.ParseRaw(result.StandardOutput);
            var ipAddress = ReadStringProperty(element, "IPAddress");
            var name = ReadStringProperty(element, "Name");

            if (!string.IsNullOrWhiteSpace(ipAddress))
            {
                return new HealthCheckStep(StepName(command.DnsResolution), CheckStatus.Passed, (name ?? command.ServerFqdn) + " résolu en " + ipAddress + ".", duration);
            }

            return new HealthCheckStep(StepName(command.DnsResolution), CheckStatus.Warning, "Le nom a été résolu mais aucune adresse IP n'a été retournée.", duration);
        }

        HealthCheckStep OnModulesSuccess(PowerShellExecutionResult result, TimeSpan duration)
        {
            foreach (var module in _parser.ParseModules(result.StandardOutput))
            {
                modules.Add(module.Name);
            }

            if (modules.Contains("GroupPolicy") || modules.Contains("ActiveDirectory"))
            {
                var detail = string.Join(", ", modules.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
                return new HealthCheckStep(StepName(command.LocalModules), CheckStatus.Passed, "Modules disponibles localement : " + detail + ".", duration);
            }

            return new HealthCheckStep(
                StepName(command.LocalModules),
                CheckStatus.Warning,
                "Modules GroupPolicy/ActiveDirectory absents de ce poste : les commandes s'exécuteront à distance sur le serveur cible.",
                duration,
                "Pour exécuter en local, installez RSAT : Add-WindowsCapability -Online -Name Rsat.GroupPolicy.Management~~~~0.0.1.0");
        }

        HealthCheckStep OnWinRmSuccess(PowerShellExecutionResult result, TimeSpan duration)
        {
            var element = _parser.ParseRaw(result.StandardOutput);
            var vendor = ReadStringProperty(element, "ProductVendor");
            var version = ReadStringProperty(element, "ProductVersion");
            var detail = string.IsNullOrWhiteSpace(vendor)
                ? string.Empty
                : " (" + vendor + (string.IsNullOrWhiteSpace(version) ? string.Empty : " " + version) + ")";

            // Le canal réellement disponible est annoncé : sur une plateforme de
            // test, l'absence d'écouteur 5986 est la norme, pas une anomalie.
            var channel = result.TransportUsed switch
            {
                WinRmTransportMode.Https => "HTTPS " + WinRmPorts.Https + " (chiffré)",
                WinRmTransportMode.Http => "HTTP " + WinRmPorts.Http + " (en clair)",
                _ => string.Empty,
            };

            var message = "WinRM actif et accepte les connexions"
                + (string.IsNullOrWhiteSpace(channel) ? string.Empty : " sur le canal " + channel)
                + detail + ".";

            // Un repli Auto → HTTP est un affaiblissement du canal : il doit rester
            // visible dans le rapport de santé, pas seulement dans le journal.
            if (result.TransportFallbackOccurred)
            {
                return new HealthCheckStep(
                    StepName(command.WinRmService),
                    CheckStatus.Warning,
                    message + " Repli automatique depuis HTTPS : " + result.TransportFallbackReason + ".",
                    duration,
                    "Le canal WinRM n'est pas chiffré. Pour le chiffrer, créez un écouteur HTTPS sur le contrôleur "
                    + "(port " + WinRmPorts.Https + ") avec un certificat émis par une autorité de confiance, puis "
                    + "choisissez « HTTPS strict » dans Paramètres.");
            }

            return new HealthCheckStep(StepName(command.WinRmService), CheckStatus.Passed, message, duration);
        }

        HealthCheckStep OnAuthenticationSuccess(PowerShellExecutionResult result, TimeSpan duration)
        {
            var username = _parser.ParseScalarString(result.StandardOutput);
            var identity = string.IsNullOrWhiteSpace(username) ? "utilisateur courant" : username;
            return new HealthCheckStep(
                StepName(command.Authentication),
                CheckStatus.Passed,
                "Authentification réussie sur la cible en tant que " + identity + ".",
                duration);
        }

        HealthCheckStep OnAdAccessSuccess(PowerShellExecutionResult result, TimeSpan duration)
        {
            var element = _parser.ParseRaw(result.StandardOutput);
            var domainName = ReadStringProperty(element, "Name");
            var netbios = ReadStringProperty(element, "NetBIOSName");
            var detail = "accès en lecture à AD DS confirmé";
            if (!string.IsNullOrWhiteSpace(domainName))
            {
                detail += " (domaine " + domainName + (string.IsNullOrWhiteSpace(netbios) ? string.Empty : " / NetBIOS " + netbios) + ")";
            }

            return new HealthCheckStep(StepName(command.AdAccess), CheckStatus.Passed, detail + ".", duration);
        }

        HealthCheckStep OnGroupPolicyCapabilitySuccess(PowerShellExecutionResult result, TimeSpan duration)
        {
            var element = _parser.ParseRaw(result.StandardOutput);
            var count = ReadStringProperty(element, "Count");
            return new HealthCheckStep(
                StepName(command.GroupPolicyCapability),
                CheckStatus.Passed,
                "Module GroupPolicy opérationnel : " + (string.IsNullOrWhiteSpace(count) ? "nombre de GPO non retourné" : count + " GPO dans le domaine") + ".",
                duration);
        }

        // --- Déroulé du Health Check ---

        // 1. Résolution DNS (locale)
        var dns = await RunStepAsync(command.DnsResolution, _executors.Local, OnDnsSuccess, cancellationToken);
        steps.Add(dns);
        progress?.Report(dns);

        // 2. Modules PowerShell locaux (RSAT) — détermine où les étapes suivantes s'exécutent
        var modulesStep = await RunStepAsync(command.LocalModules, _executors.Local, OnModulesSuccess, cancellationToken);
        steps.Add(modulesStep);
        progress?.Report(modulesStep);

        var hasLocalAdModule = modules.Contains("ActiveDirectory");
        var hasLocalGpoModule = modules.Contains("GroupPolicy");

        // 3. Service WinRM (Test-WSMan interroge la cible)
        //
        // Si les modules RSAT sont présents sur ce poste, l'exécution locale est
        // possible sans WinRM : un échec WinRM devient alors un simple avertissement
        // (typiquement un contrôleur de domaine de test dont PSRemoting n'a jamais
        // été activé) au lieu d'un blocage de connexion.
        var (winRmStep, winRmResult) = await ExecuteStepAsync(
            command.WinRmService,
            remote,
            OnWinRmSuccess,
            cancellationToken,
            hasLocalGpoModule);
        steps.Add(winRmStep);
        progress?.Report(winRmStep);

        // Un repli en clair reste un succès fonctionnel : WinRM est joignable,
        // seul le canal est dégradé. Les étapes suivantes doivent donc s'exécuter,
        // sinon le repli réussi se traduirait à tort par des tests « Skipped ».
        var winRmReachable = winRmStep.Status == CheckStatus.Passed
            || winRmResult?.TransportFallbackOccurred == true;

        // 4. Authentification : inutile (et long) si WinRM ne répond pas
        var authentication = winRmReachable
            ? await RunStepAsync(command.Authentication, remote, OnAuthenticationSuccess, cancellationToken)
            : HealthCheckStep.Skipped(
                "Authentification (Invoke-Command)",
                hasLocalGpoModule
                    ? "Non testée : WinRM ne répond pas sur la cible. Sans effet, l'exécution se fera en local (RSAT) avec le compte Windows de cette session."
                    : "Non testée : le service WinRM ne répond pas sur la cible.");
        steps.Add(authentication);
        progress?.Report(authentication);

        // 5. Accès AD DS : en local si le module ActiveDirectory est présent, sinon à distance
        var adExecutor = hasLocalAdModule ? _executors.Local : remote;
        var adAccess = (hasLocalAdModule || authentication.Status == CheckStatus.Passed)
            ? await RunStepAsync(command.AdAccess, adExecutor, OnAdAccessSuccess, cancellationToken)
            : HealthCheckStep.Skipped("Accès AD DS (Get-ADDomain)", "Non testée : aucun module ActiveDirectory local et aucune authentification distante.");
        steps.Add(adAccess);
        progress?.Report(adAccess);

        // 6. Capacité GroupPolicy : en local si le module GroupPolicy est présent, sinon à distance
        var gpoExecutor = hasLocalGpoModule ? _executors.Local : remote;
        var gpoCapability = (hasLocalGpoModule || authentication.Status == CheckStatus.Passed)
            ? await RunStepAsync(command.GroupPolicyCapability, gpoExecutor, OnGroupPolicyCapabilitySuccess, cancellationToken)
            : HealthCheckStep.Skipped("GroupPolicy (Get-GPO -All)", "Non testée : aucun module GroupPolicy local et aucune authentification distante.");
        steps.Add(gpoCapability);
        progress?.Report(gpoCapability);

        // --- Synthèse et mode d'exécution recommandé ---

        var canRunLocally = hasLocalGpoModule;
        var canRunRemotely = authentication.Status == CheckStatus.Passed;
        var dnsOk = dns.Status == CheckStatus.Passed;
        var adOk = adAccess.Status == CheckStatus.Passed;
        var gpoOk = gpoCapability.Status == CheckStatus.Passed;

        // Si un compte administrateur explicite a été fourni et authentifié à
        // distance, le mode distant est privilégié : c'est ce compte qui doit
        // exécuter les commandes. Sans cela, la présence de RSAT en local
        // ferait exécuter les commandes avec l'utilisateur Windows courant,
        // qui peut ne pas disposer des droits d'administration du domaine.
        var explicitCredentials = credentials?.Mode == CredentialMode.Explicit;
        var recommended = explicitCredentials && canRunRemotely
            ? ExecutorMode.Remote
            : canRunLocally
                ? ExecutorMode.Local
                : canRunRemotely
                    ? ExecutorMode.Remote
                    : ExecutorMode.Local;
        var isHealthy = dnsOk && adOk && gpoOk;

        // Un repli de canal négocié en clair est rappelé dans la synthèse :
        // c'est la seule information que l'utilisateur lit avant de valider
        // une connexion en mode distant.
        var insecureChannel = steps.Any(step =>
            step.Status == CheckStatus.Warning
            && step.Message.Contains("Repli automatique depuis HTTPS", StringComparison.Ordinal));

        var summary = isHealthy
            ? "Connexion à " + command.ServerFqdn + " réussie. Mode d'exécution recommandé : "
              + (recommended == ExecutorMode.Local ? "local (RSAT)" : "distant (WinRM)")
              + (insecureChannel ? " (canal WinRM en clair après repli depuis HTTPS)." : ".")
            : BuildFailureSummary(command.ServerFqdn, dnsOk, canRunLocally, canRunRemotely);

        return new HealthCheckReport(command.ServerFqdn, domainFqdn, steps, isHealthy, recommended, summary);
    }

    /// <summary>
    /// Résumé d'échec actionnable. Le cas le plus fréquent sur un poste client
    /// sans RSAT est l'absence totale de canal d'exécution (ni local, ni distant) :
    /// ce n'est pas une erreur de saisie, il faut installer RSAT OU activer WinRM.
    /// Le message nomme explicitement les deux correctifs pour que l'administrateur
    /// n'ait pas à deviner d'où vient le blocage.
    /// </summary>
    private static string BuildFailureSummary(string serverFqdn, bool dnsOk, bool canRunLocally, bool canRunRemotely)
    {
        if (!dnsOk)
        {
            return "Connexion échouée : le nom « " + serverFqdn + " » ne se résout pas en DNS. "
                + "Vérifiez le FQDN (saisissez le nom complet du contrôleur, pas une IP si possible) et que ce poste utilise bien le DNS du domaine.";
        }

        if (!canRunLocally && !canRunRemotely)
        {
            return "Connexion échouée : ce poste n'a AUCUN canal d'exécution vers le domaine — ni module RSAT local, ni WinRM distant joignable. "
                + "Deux solutions : (A) installer RSAT sur ce poste puis exécuter en local avec votre compte Windows : "
                + "Add-WindowsCapability -Online -Name Rsat.GroupPolicy.Management~~~~0.0.1.0 et Rsat.ActiveDirectory.DS-LDS.Tools~~~~0.0.1.0 ; "
                + "(B) activer la gestion à distance sur le contrôleur : « Enable-PSRemoting -Force » en PowerShell administrateur sur " + serverFqdn
                + " (ports 5985/5986). Voir le détail des étapes pour la cause exacte.";
        }

        return "Connexion échouée : l'accès AD DS ou GroupPolicy n'a pas pu être confirmé. Voir le détail des étapes pour l'origine précise.";
    }

    /// <param name="degradeToWarningWhen">
    /// Condition sous laquelle un échec est ramené au rang d'avertissement.
    /// Utilisé pour WinRM : quand RSAT est disponible en local, l'indisponibilité
    /// de WinRM n'empêche pas l'application de fonctionner, elle limite seulement
    /// le mode d'exécution. Les deux résultats sont conservés : l'échec et son
    /// diagnostic restent visibles, seule la gravité change.
    /// </param>
    private async Task<HealthCheckStep> RunStepAsync(
        PowerShellCommand command,
        IPowerShellExecutor executor,
        Func<PowerShellExecutionResult, TimeSpan, HealthCheckStep> onSuccess,
        CancellationToken cancellationToken)
    {
        var (step, _) = await ExecuteStepAsync(command, executor, onSuccess, cancellationToken);
        return step;
    }

    /// <summary>
    /// Exécute une étape et restitue son résultat brut en plus de l'étape
    /// analysée. Le résultat brut est nécessaire pour WinRM : un repli
    /// HTTPS → HTTP abouti produit un avertissement alors que le canal reste
    /// fonctionnel, ce que seul le résultat brut permet de distinguer d'un
    /// échec réel.
    /// </summary>
    private async Task<(HealthCheckStep Step, PowerShellExecutionResult? Result)> ExecuteStepAsync(
        PowerShellCommand command,
        IPowerShellExecutor executor,
        Func<PowerShellExecutionResult, TimeSpan, HealthCheckStep> onSuccess,
        CancellationToken cancellationToken,
        bool degradeToWarningWhen = false)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await executor.ExecuteAsync(command, cancellationToken);
            stopwatch.Stop();

            if (result.TimedOut || result.ExitCode != 0)
            {
                var error = _interpreter.Interpret(result, command.Name);
                if (degradeToWarningWhen)
                {
                    return (new HealthCheckStep(
                        StepName(command),
                        CheckStatus.Warning,
                        error.Title + " Non bloquant : l'exécution se fera en local (RSAT).",
                        stopwatch.Elapsed,
                        error.Remediation), result);
                }

                return (new HealthCheckStep(StepName(command), CheckStatus.Failed, error.Title, stopwatch.Elapsed, error.Remediation), result);
            }

            return (onSuccess(result, stopwatch.Elapsed), result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return (HealthCheckStep.Skipped(StepName(command), "Test annulé."), null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.Error("Health check step failed: " + command.Name, ex);
            return (new HealthCheckStep(StepName(command), CheckStatus.Failed, "Erreur inattendue : " + ex.Message, stopwatch.Elapsed, "Consulter le fichier journal (logs/adops.log)."), null);
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

        // Certaines valeurs (ex. : Count) sont numériques.
        if (element.Value.TryGetProperty(propertyName, out var raw))
        {
            return raw.ValueKind switch
            {
                JsonValueKind.Number => raw.GetRawText(),
                JsonValueKind.True => "True",
                JsonValueKind.False => "False",
                _ => null,
            };
        }

        return null;
    }

    private static string StepName(PowerShellCommand command) => command.Name switch
    {
        "DnsResolution" => "Résolution DNS (Resolve-DnsName)",
        "LocalModules" => "Modules PowerShell locaux (RSAT)",
        "WinRmService" => "Service WinRM (Test-WSMan)",
        "Authentication" => "Authentification (Invoke-Command)",
        "AdAccess" => "Accès AD DS (Get-ADDomain)",
        "GroupPolicyCapability" => "GroupPolicy (Get-GPO -All)",
        _ => command.Name,
    };
}
