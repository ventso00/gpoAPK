using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Network;
using ADOps.GpoManager.Core.Services;
using ADOps.GpoManager.Core.Validation;
using ADOps.GpoManager.Infrastructure.Configuration;

namespace ADOps.GpoManager.Infrastructure.Services;

/// <summary>
/// Détection automatique de la session de domaine (remplacement de l'écran de
/// connexion). La séquence est :
/// 1. identité de la session Windows (compte, élévation) ;
/// 2. cible : le contrôleur et le domaine **configurés** dans settings.json
///    (ils sont prioritaires), sinon auto-détectés par l'API Netlogon ;
/// 3. contrôle de santé complet (DNS, RSAT, WinRM, AD DS, GroupPolicy) ;
/// 4. construction d'un <see cref="DomainContext"/> en mode local, avec les
///    identifiants du compte courant (aucun secret en mémoire).
/// La cible résolue est réécrite dans settings.json : le démarrage suivant est
/// donc déterministe. L'échec n'est jamais silencieux : il produit un message
/// actionnable.
/// </summary>
public sealed class SessionBootstrapper : ISessionBootstrapper
{
    private readonly IAuthenticationService _authentication;
    private readonly IAuditService _audit;
    private readonly IConfigurationStore _configurationStore;
    private readonly ILogger _logger;

    public SessionBootstrapper(
        IAuthenticationService authentication,
        IAuditService audit,
        IConfigurationStore configurationStore,
        ILogger logger)
    {
        _authentication = authentication;
        _audit = audit;
        _configurationStore = configurationStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SessionBootstrapResult> BootstrapAsync(
        IProgress<HealthCheckStep>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var identity = WindowsSessionProbe.Detect();
        var warnings = new List<string>();
        var configuration = _configurationStore.Load();

        _logger.Information(
            "Session | détection | compte=" + identity.DisplayText
            + " | élevé=" + identity.IsElevated
            + " | domaine=" + (identity.DomainName ?? "(aucun)")
            + " | source=" + identity.DomainSource);

        if (!identity.IsElevated)
        {
            warnings.Add(
                "La session n'est pas élevée : la création de liens de GPO (New-GPLink) "
                + "exige les droits d'administrateur. Relancez l'application en tant qu'administrateur.");
        }

        // --- Domaine : configuration d'abord, auto-détection en repli ---
        var domainFqdn = Normalize(configuration.DomainFqdn)
            ?? Normalize(identity.DomainName);

        if (domainFqdn is null)
        {
            var error =
                "Aucun domaine n'est joignable : ni configuration (settings.json), ni adhésion d'un "
                + "domaine Active Directory détectée sur cette machine. Vérifiez que le poste est joint "
                + "au domaine, ou renseignez le domaine dans Paramètres ▸ Cible du domaine.";

            await AuditAsync(identity, AuditResult.Failure, error, null).ConfigureAwait(false);
            return new SessionBootstrapResult
            {
                Success = false,
                Identity = identity,
                Error = error,
                Warnings = warnings,
            };
        }

        // --- Contrôleur : configuration d'abord, Netlogon en repli ---
        var serverFqdn = ResolveServer(configuration, domainFqdn, warnings);

        HealthCheckReport report;
        try
        {
            report = await _authentication.RunDiagnosticsAsync(
                serverFqdn,
                domainFqdn,
                credentials: null,
                progress: progress,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            var cancelled = "Détection de session annulée.";
            await AuditAsync(identity, AuditResult.Warning, cancelled, null).ConfigureAwait(false);
            return new SessionBootstrapResult
            {
                Success = false,
                Identity = identity,
                Error = cancelled,
                Warnings = warnings,
            };
        }
        catch (Exception ex)
        {
            _logger.Error("Détection de session échouée", ex);
            var error = "Détection de session impossible : " + ex.Message;
            await AuditAsync(identity, AuditResult.Failure, error, ex.ToString()).ConfigureAwait(false);
            return new SessionBootstrapResult
            {
                Success = false,
                Identity = identity,
                Error = error,
                Warnings = warnings,
            };
        }

        if (!report.IsHealthy)
        {
            await AuditAsync(identity, AuditResult.Failure, report.Summary, null).ConfigureAwait(false);
            return new SessionBootstrapResult
            {
                Success = false,
                Identity = identity,
                Report = report,
                Error = report.Summary,
                Warnings = warnings,
            };
        }

        // Mode local (RSAT) avec le compte Windows de la session : c'est le mode
        // recommandé et il ne requiert aucun secret.
        var context = new DomainContext(
            serverFqdn,
            domainFqdn,
            domainNetbios: configuration.DomainNetbios,
            executorMode: ExecutorMode.Local,
            userName: identity.UserName,
            connectedAtUtc: DateTime.UtcNow,
            credentials: new ConnectionCredentials(),
            lastHealthCheck: report);

        PersistTarget(configuration, serverFqdn, domainFqdn, identity.DcAddress);

        await AuditAsync(
            identity,
            AuditResult.Success,
            "Session implicite établie : " + identity.DisplayText + " — domaine " + domainFqdn
                + " via " + serverFqdn + " (mode local, compte Windows courant).",
            report.Summary).ConfigureAwait(false);

        _logger.Information("Session | contexte établi | " + domainFqdn + " | " + serverFqdn);

        return new SessionBootstrapResult
        {
            Success = true,
            Identity = identity,
            Context = context,
            Report = report,
            Warnings = warnings,
        };
    }

    /// <summary>
    /// Détermine le contrôleur cible. Priorité à la configuration ; l'API
    /// Netlogon ne sert que de repli. Une adresse IP est convertie en FQDN
    /// (Kerberos refuse une cible IP) ; si la conversion échoue, l'IP est
    /// conservée et l'avertissement explique la conséquence.
    /// </summary>
    private static string ResolveServer(AppConfiguration configuration, string domainFqdn, List<string> warnings)
    {
        var configured = configuration.ServerFqdn?.Trim();
        var address = configuration.ServerAddress?.Trim();

        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = string.IsNullOrWhiteSpace(address) ? null : address;
        }

        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (DomainValidator.IsIPv4Address(configured))
            {
                var resolved = DcLocator.MatchByAddress(configured, domainFqdn);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    return resolved;
                }

                warnings.Add(
                    "Le contrôleur n'a pas pu être résolu depuis l'adresse " + configured
                    + " (aucune correspondance Netlogon) : l'adresse IP est utilisée comme cible. "
                    + "Kerberos exige un nom DNS : renseignez le FQDN du contrôleur dans Paramètres.");
                return configured;
            }

            return DomainValidator.NormalizeFqdn(configured);
        }

        var discovered = DcLocator.Discover(domainFqdn);
        if (discovered is not null && !string.IsNullOrWhiteSpace(discovered.Fqdn))
        {
            return discovered.Fqdn;
        }

        warnings.Add(
            "Le contrôleur de domaine n'a pas pu être localisé par Netlogon : la machine locale ("
            + Environment.MachineName + ") est utilisée comme cible. Si la collecte échoue, vérifiez "
            + "le DNS et l'accessibilité du contrôleur.");
        return Environment.MachineName;
    }

    /// <summary>
    /// Réécrit la cible résolue dans settings.json : les démarrages suivants
    /// utilisent exactement le même contrôleur.
    /// </summary>
    private void PersistTarget(
        AppConfiguration configuration,
        string serverFqdn,
        string domainFqdn,
        string? discoveredAddress)
    {
        try
        {
            var address = configuration.ServerAddress;
            if (string.IsNullOrWhiteSpace(address) && !string.IsNullOrWhiteSpace(discoveredAddress))
            {
                address = discoveredAddress;
            }

            if (string.Equals(configuration.ServerFqdn, serverFqdn, StringComparison.OrdinalIgnoreCase)
                && string.Equals(configuration.DomainFqdn, domainFqdn, StringComparison.OrdinalIgnoreCase)
                && string.Equals(configuration.ServerAddress, address, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _configurationStore.Save(configuration with
            {
                ServerFqdn = serverFqdn,
                DomainFqdn = domainFqdn,
                ServerAddress = address ?? string.Empty,
            });
        }
        catch (Exception ex)
        {
            _logger.Warning("Enregistrement de la cible impossible : " + ex.Message);
        }
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var candidate = value.Trim().TrimEnd('.');
        var (isValid, _) = DomainValidator.ValidateDomainName(candidate);
        return isValid ? candidate.ToLowerInvariant() : null;
    }

    private Task AuditAsync(SessionIdentity identity, AuditResult result, string message, string? details)
        => _audit.LogAsync(
            AuditActions.Login,
            identity.DomainName ?? Environment.MachineName,
            result,
            message,
            details,
            new AuditContext(
                Server: Environment.MachineName,
                Domain: identity.DomainName,
                RequestedState: "Session implicite (compte Windows courant)",
                OperationId: Guid.NewGuid().ToString("N")));
}
