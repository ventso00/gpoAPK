using System.Globalization;
using ADOps.GpoManager.Core.Commands;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Services;
using ADOps.GpoManager.PowerShell.Host;

namespace ADOps.GpoManager.PowerShell.Services;

/// <summary>Service GPO : exécute la commande typée ListGposCommand et audite chaque opération.</summary>
public sealed class GpoService : IGpoService
{
    private readonly IPowerShellExecutorFactory _executors;
    private readonly PowerShellOutputParser _parser;
    private readonly IErrorInterpreter _interpreter;
    private readonly IAuditService _audit;
    private readonly ILogger _logger;

    public GpoService(
        IPowerShellExecutorFactory executors,
        PowerShellOutputParser parser,
        IErrorInterpreter interpreter,
        IAuditService audit,
        ILogger logger)
    {
        _executors = executors;
        _parser = parser;
        _interpreter = interpreter;
        _audit = audit;
        _logger = logger;
    }

    public async Task<OperationResult<IReadOnlyList<GpoSummary>>> ListGposAsync(DomainContext context, CancellationToken cancellationToken = default)
    {
        var executor = context.ExecutorMode == ExecutorMode.Remote
            ? _executors.CreateRemote(context.ServerFqdn, context.Credentials)
            : _executors.Local;
        var command = new ListGposCommand();

        _logger.Information("ListGpos | cible=" + context.ServerFqdn + " | mode=" + context.ExecutorMode);

        try
        {
            var result = await executor.ExecuteAsync(command, cancellationToken);

            if (result.ExitCode != 0 || result.TimedOut)
            {
                var error = _interpreter.Interpret(result, "Liste des GPO");
                await _audit.LogAsync(
                    AuditActions.GpoList,
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
                return OperationResult<IReadOnlyList<GpoSummary>>.Failure(error);
            }

            var gpos = _parser.ParseGpoList(result.StandardOutput);
            var message = gpos.Count == 0
                ? "Aucune GPO dans le domaine " + context.DomainFqdn + "."
                : gpos.Count + " GPO(s) chargée(s) depuis " + context.DomainFqdn + ".";

            await _audit.LogAsync(
                AuditActions.GpoList,
                context.ServerFqdn,
                AuditResult.Success,
                message,
                "Durée : " + result.Duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s",
                new AuditContext(
                    Server: context.ServerFqdn,
                    Domain: context.DomainFqdn,
                    Duration: result.Duration,
                    OperationId: Guid.NewGuid().ToString("N")));

            _logger.Information(message);
            return OperationResult<IReadOnlyList<GpoSummary>>.Success(gpos);
        }
        catch (OperationCanceledException)
        {
            await _audit.LogAsync(AuditActions.GpoList, context.ServerFqdn, AuditResult.Warning, "Liste des GPO annulée par l'utilisateur.");
            return OperationResult<IReadOnlyList<GpoSummary>>.Failure(new OperationError("Opération annulée", "L'utilisateur a annulé l'opération."));
        }
        catch (Exception ex)
        {
            _logger.Error("Erreur ListGpos", ex);
            var error = new OperationError("Erreur inattendue lors du chargement des GPO", ex.Message, "Consulter le fichier journal (logs/adops.log).");
            await _audit.LogAsync(AuditActions.GpoList, context.ServerFqdn, AuditResult.Failure, error.Title, ex.ToString());
            return OperationResult<IReadOnlyList<GpoSummary>>.Failure(error);
        }
    }
}
