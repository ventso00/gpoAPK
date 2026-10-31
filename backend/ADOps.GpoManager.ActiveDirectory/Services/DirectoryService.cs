using ADOps.GpoManager.Core.Commands;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Services;
using ADOps.GpoManager.PowerShell;
using ADOps.GpoManager.PowerShell.Host;

namespace ADOps.GpoManager.ActiveDirectory;

/// <summary>
/// Annuaire : lecture des utilisateurs du domaine via Get-ADUser
/// (commande typée). Chaque lecture est indépendante : un échec
/// produit une erreur structurée, jamais une donnée inventée.
/// </summary>
public sealed class DirectoryService : IDirectoryService
{
    private readonly IPowerShellExecutorFactory _executors;
    private readonly IErrorInterpreter _interpreter;
    private readonly IAuditService _audit;
    private readonly ILogger _logger;
    private readonly AdOutputParser _parser = new();

    public DirectoryService(
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

    public async Task<OperationResult<IReadOnlyList<UserInfo>>> ListUsersAsync(
        DomainContext context,
        CancellationToken cancellationToken = default)
    {
        var executor = context.ExecutorMode == ExecutorMode.Remote
            ? _executors.CreateRemote(context.ServerFqdn, context.Credentials)
            : _executors.Local;
        var command = new ListUsersCommand();

        _logger.Information("ListUsers | cible=" + context.ServerFqdn + " | mode=" + context.ExecutorMode);

        try
        {
            var result = await executor.ExecuteAsync(command, cancellationToken);

            if (result.ExitCode != 0 || result.TimedOut)
            {
                var error = _interpreter.Interpret(result, "Lecture des utilisateurs");
                await _audit.LogAsync(
                    AuditActions.DirectoryRead,
                    context.ServerFqdn,
                    AuditResult.Failure,
                    error.Title,
                    result.StandardError,
                    new AuditContext(
                        Server: context.ServerFqdn,
                        Domain: context.DomainFqdn,
                        ErrorCode: "ExitCode=" + result.ExitCode,
                        Duration: result.Duration,
                        OperationId: Guid.NewGuid().ToString("N")));
                return OperationResult<IReadOnlyList<UserInfo>>.Failure(error);
            }

            var administratorDns = await ReadAdministratorDnsAsync(executor, cancellationToken).ConfigureAwait(false);
            var users = _parser.ParseUsers(result.StandardOutput, administratorDns);
            var message = users.Count == 0
                ? "Aucun utilisateur dans le domaine " + context.DomainFqdn + "."
                : users.Count + " utilisateur(s) lu(s) dans " + context.DomainFqdn + ".";

            await _audit.LogAsync(
                AuditActions.DirectoryRead,
                context.ServerFqdn,
                AuditResult.Success,
                message,
                "Durée : " + result.Duration.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s",
                new AuditContext(
                    Server: context.ServerFqdn,
                    Domain: context.DomainFqdn,
                    Duration: result.Duration,
                    OperationId: Guid.NewGuid().ToString("N")));

            _logger.Information(message);
            return OperationResult<IReadOnlyList<UserInfo>>.Success(users);
        }        catch (OperationCanceledException)
        {
            await _audit.LogAsync(
                AuditActions.DirectoryRead,
                context.ServerFqdn,
                AuditResult.Warning,
                "Lecture des utilisateurs annulée par l'utilisateur.");
            return OperationResult<IReadOnlyList<UserInfo>>.Failure(
                new OperationError("Opération annulée", "L'utilisateur a annulé l'opération."));
        }
        catch (Exception ex)
        {
            _logger.Error("Erreur ListUsers", ex);
            var error = new OperationError(
                "Erreur inattendue lors de la lecture des utilisateurs",
                ex.Message,
                "Consulter le fichier journal (logs/adops.log).");
            await _audit.LogAsync(
                AuditActions.DirectoryRead,
                context.ServerFqdn,
                AuditResult.Failure,
                error.Title,
                ex.ToString());
            return OperationResult<IReadOnlyList<UserInfo>>.Failure(error);
        }
    }

    /// <summary>
    /// Déplace un utilisateur vers une autre unité d'organisation via
    /// <c>Move-ADObject</c>. Utilisé pour sortir un compte du conteneur
    /// par défaut CN=Users vers une OU dédiée où les GPO peuvent être liées.
    /// </summary>
    public async Task<OperationResult<string>> MoveToOuAsync(
        DomainContext context,
        string userDistinguishedName,
        string targetOuDistinguishedName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(userDistinguishedName))
        {
            return OperationResult<string>.Failure(new OperationError(
                "Utilisateur non précisé",
                "Le DistinguishedName de l'utilisateur est requis.",
                "Sélectionnez un utilisateur dans la liste avant de tenter le déplacement."));
        }

        if (string.IsNullOrWhiteSpace(targetOuDistinguishedName))
        {
            return OperationResult<string>.Failure(new OperationError(
                "OU cible non précisée",
                "Le DistinguishedName de l'unité d'organisation cible est requis.",
                "Sélectionnez une OU cible dans l'arborescence AD DS."));
        }

        var executor = context.ExecutorMode == ExecutorMode.Remote
            ? _executors.CreateRemote(context.ServerFqdn, context.Credentials)
            : _executors.Local;

        _logger.Information("MoveUserToOu | utilisateur=" + userDistinguishedName + " | cible=" + targetOuDistinguishedName + " | mode=" + context.ExecutorMode);

        try
        {
            var result = await executor.ExecuteAsync(
                new MoveAdUserCommand(userDistinguishedName.Trim(), targetOuDistinguishedName.Trim()),
                cancellationToken).ConfigureAwait(false);

            if (result.ExitCode != 0 || result.TimedOut)
            {
                var error = _interpreter.Interpret(result, "Déplacement de l'utilisateur vers l'OU");
                await _audit.LogAsync(
                    AuditActions.DirectoryWrite,
                    context.ServerFqdn,
                    AuditResult.Failure,
                    error.Title,
                    result.StandardError,
                    new AuditContext(
                        Server: context.ServerFqdn,
                        Domain: context.DomainFqdn,
                        ErrorCode: "ExitCode=" + result.ExitCode,
                        Duration: result.Duration,
                        OperationId: Guid.NewGuid().ToString("N"))).ConfigureAwait(false);
                return OperationResult<string>.Failure(error);
            }

            var message = "Utilisateur « " + userDistinguishedName + " » déplacé vers « " + targetOuDistinguishedName + " ». "
                + "Les GPO liées à la nouvelle OU s'appliqueront au prochain gpupdate.";
            await _audit.LogAsync(
                AuditActions.DirectoryWrite,
                context.ServerFqdn,
                AuditResult.Success,
                message,
                "Utilisateur déplacé avec succès.",
                new AuditContext(
                    Server: context.ServerFqdn,
                    Domain: context.DomainFqdn,
                    Duration: result.Duration,
                    OperationId: Guid.NewGuid().ToString("N"))).ConfigureAwait(false);

            _logger.Information(message);
            return OperationResult<string>.Success(message);
        }
        catch (OperationCanceledException)
        {
            await _audit.LogAsync(
                AuditActions.DirectoryWrite,
                context.ServerFqdn,
                AuditResult.Warning,
                "Déplacement de l'utilisateur annulé par l'utilisateur.").ConfigureAwait(false);
            return OperationResult<string>.Failure(
                new OperationError("Opération annulée", "L'utilisateur a annulé l'opération."));
        }
        catch (Exception ex)
        {
            _logger.Error("Erreur MoveToOuAsync", ex);
            var error = new OperationError(
                "Erreur inattendue lors du déplacement de l'utilisateur",
                ex.Message,
                "Consulter le fichier journal (logs/adops.log).");
            await _audit.LogAsync(
                AuditActions.DirectoryWrite,
                context.ServerFqdn,
                AuditResult.Failure,
                error.Title,
                ex.ToString()).ConfigureAwait(false);
            return OperationResult<string>.Failure(error);
        }
    }

    /// <summary>
    /// DistinguishedNames des membres des groupes d'administration du domaine.
    /// Un échec est toléré : les comptes sont alors classés « standard ».
    /// </summary>
    private async Task<IReadOnlySet<string>> ReadAdministratorDnsAsync(
        IPowerShellExecutor executor,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await executor.ExecuteAsync(new ListDomainAdministratorsCommand(), cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0 || result.TimedOut)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            return _parser.ParseAdministratorDns(result.StandardOutput);
        }
        catch (Exception ex)
        {
            _logger.Warning("Classification administrateur indisponible : " + ex.Message);
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
