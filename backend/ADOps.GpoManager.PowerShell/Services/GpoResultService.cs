using ADOps.GpoManager.Core.Commands;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Services;
using ADOps.GpoManager.PowerShell.Host;

namespace ADOps.GpoManager.PowerShell.Services;

/// <summary>
/// Obtention des résultats effectifs (RSoP) d'un ordinateur via gpresult,
/// exécuté sur la machine cible à travers WinRM. Le résultat n'est jamais deviné :
/// si le rapport ne peut être obtenu, l'erreur est explicite.
/// </summary>
public sealed class GpoResultService : IGpoResultService
{
    private readonly IPowerShellExecutorFactory _executors;
    private readonly IErrorInterpreter _interpreter;
    private readonly ILogger _logger;

    public GpoResultService(IPowerShellExecutorFactory executors, IErrorInterpreter interpreter, ILogger logger)
    {
        _executors = executors;
        _interpreter = interpreter;
        _logger = logger;
    }

    public async Task<OperationResult<ComputerGpoResult>> GetResultantSetAsync(string computerName, DomainContext context, CancellationToken cancellationToken = default)
    {
        var executor = _executors.CreateRemote(computerName, context.Credentials);
        var command = new GetGpoResultantSetCommand(computerName);

        try
        {
            var result = await executor.ExecuteAsync(command, cancellationToken);

            var rawReport = GpResultReportParser.ExtractXmlReport(result.StandardOutput);
            var reportAvailable = rawReport is not null;

            if ((result.ExitCode != 0 && !reportAvailable) || result.TimedOut)
            {
                var error = _interpreter.Interpret(result, "Rapport RSoP (gpresult)");
                return OperationResult<ComputerGpoResult>.Failure(new OperationError(
                    "Rapport RSoP indisponible pour " + computerName,
                    error.Title,
                    error.Remediation ?? "Vérifiez que le poste est joignable via WinRM et que le compte dispose des droits nécessaires.",
                    result.StandardError));
            }

            if (result.ExitCode != 0)
            {
                _logger.Warning(
                    "RSoP " + computerName + " : rapport lu malgré un code de sortie " + result.ExitCode
                    + " (bruit de transport WinRM).");
            }

            var appliedGpos = GpResultReportParser.ParseAppliedGpoNamesFromXml(rawReport);

            return OperationResult<ComputerGpoResult>.Success(new ComputerGpoResult
            {
                ComputerName = computerName,
                ReportDateUtc = DateTimeOffset.UtcNow,
                ReportSource = "gpresult /SCOPE COMPUTER (via WinRM sur " + computerName + ")",
                AppliedGpoNames = appliedGpos,
                RawReport = rawReport ?? string.Empty,
            });
        }
        catch (Exception ex)
        {
            _logger.Error("Erreur RSoP pour " + computerName, ex);
            return OperationResult<ComputerGpoResult>.Failure(new OperationError(
                "Erreur inattendue lors du rapport RSoP",
                ex.Message,
                "Consulter le fichier journal (logs/adops.log)."));
        }
    }
}
