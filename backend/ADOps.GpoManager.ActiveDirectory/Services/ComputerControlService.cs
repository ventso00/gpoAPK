using System.Globalization;
using ADOps.GpoManager.Core.Commands;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Services;
using ADOps.GpoManager.PowerShell;
using ADOps.GpoManager.PowerShell.Host;

namespace ADOps.GpoManager.ActiveDirectory;

/// <summary>
/// Contrôle réel des postes : les modifications AD DS passent par des
/// commandes typées exécutées sur le contrôleur (ou en local), les actions
/// de puissance par WinRM sur le poste lui-même. Chaque opération est auditée.
/// </summary>
public sealed class ComputerControlService : IComputerControlService
{
    private readonly IPowerShellExecutorFactory _executors;
    private readonly IErrorInterpreter _interpreter;
    private readonly IAuditService _audit;
    private readonly ILogger _logger;

    public ComputerControlService(
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

    public Task<OperationResult<string>> SetEnabledAsync(
        DomainContext context,
        ComputerInfo computer,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var message = enabled
            ? "Compte d'ordinateur « " + computer.Name + " » activé dans AD DS."
            : "Compte d'ordinateur « " + computer.Name + " » désactivé dans AD DS. "
                + "Le poste ne pourra plus s'authentifier ni recevoir de stratégie tant que le compte reste désactivé.";

        if (!enabled && IsProtectedComputer(context, computer))
        {
            return Task.FromResult(OperationResult<string>.Failure(new OperationError(
                "Cible protégée",
                "« " + computer.Name + " » est un serveur ou le contrôleur de domaine : cette console refuse de désactiver son compte.",
                "Seuls les postes clients Windows peuvent être désactivés depuis ADOps GPO Manager.")));
        }

        return ExecuteDirectoryAsync(
            context,
            new SetAdComputerEnabledCommand(computer.DistinguishedName, enabled),
            "ComputerEnabled=" + enabled,
            message,
            enabled ? "Activation du compte « " + computer.Name + " »" : "Désactivation du compte « " + computer.Name + " »",
            cancellationToken);
    }

    public Task<OperationResult<string>> MoveToOuAsync(
        DomainContext context,
        ComputerInfo computer,
        string targetOuDistinguishedName,
        CancellationToken cancellationToken = default)
    {
        var message = "Ordinateur « " + computer.Name + " » déplacé vers « " + targetOuDistinguishedName + " ». "
            + "Les GPO héritées par la nouvelle OU s'appliqueront au prochain gpupdate.";

        if (IsProtectedComputer(context, computer))
        {
            return Task.FromResult(OperationResult<string>.Failure(new OperationError(
                "Cible protégée",
                "« " + computer.Name + " » est un serveur ou le contrôleur de domaine : son déplacement n'est pas autorisé depuis cette console.",
                "Déplacez les serveurs depuis « Utilisateurs et ordinateurs Active Directory ».")));
        }

        if (targetOuDistinguishedName.Contains("OU=Domain Controllers", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(OperationResult<string>.Failure(new OperationError(
                "Cible protégée",
                "L'unité d'organisation « Domain Controllers » est réservée aux contrôleurs de domaine.",
                "Choisissez une OU destinée aux postes clients.")));
        }

        return ExecuteDirectoryAsync(
            context,
            new MoveAdComputerCommand(computer.DistinguishedName, targetOuDistinguishedName),
            "MoveTo=" + targetOuDistinguishedName,
            message,
            "Déplacement de « " + computer.Name + " » vers " + targetOuDistinguishedName,
            cancellationToken);
    }

    public Task<OperationResult<string>> RestartAsync(
        DomainContext context,
        ComputerInfo computer,
        CancellationToken cancellationToken = default)
        => ExecutePowerAsync(context, computer, ManagedPowerAction.Restart, cancellationToken);

    public Task<OperationResult<string>> ShutdownAsync(
        DomainContext context,
        ComputerInfo computer,
        CancellationToken cancellationToken = default)
        => ExecutePowerAsync(context, computer, ManagedPowerAction.Shutdown, cancellationToken);

    private static bool IsProtectedComputer(DomainContext context, ComputerInfo computer)
    {
        if (computer.OperatingSystem?.Contains("Server", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        if (computer.DistinguishedName.Contains("OU=Domain Controllers", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var dcHost = context.ServerFqdn.Split('.', 2)[0];
        return string.Equals(computer.Name, dcHost, StringComparison.OrdinalIgnoreCase)
            || string.Equals(computer.Name, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<OperationResult<string>> ExecuteDirectoryAsync(
        DomainContext context,
        PowerShellCommand command,
        string auditDetail,
        string successMessage,
        string operationName,
        CancellationToken cancellationToken)
    {
        var executor = context.ExecutorMode == ExecutorMode.Remote
            ? _executors.CreateRemote(context.ServerFqdn, context.Credentials)
            : _executors.Local;

        try
        {
            var result = await executor.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0 || result.TimedOut)
            {
                var error = _interpreter.Interpret(result, operationName);
                await AuditAsync(context, operationName, AuditResult.Failure, error.Title, result, auditDetail)
                    .ConfigureAwait(false);
                return OperationResult<string>.Failure(error);
            }

            await AuditAsync(context, operationName, AuditResult.Success, successMessage, result, auditDetail)
                .ConfigureAwait(false);
            _logger.Information(successMessage);
            return OperationResult<string>.Success(successMessage);
        }
        catch (OperationCanceledException)
        {
            await _audit.LogAsync(AuditActions.ComputerControl, context.ServerFqdn, AuditResult.Warning,
                operationName + " annulé par l'utilisateur.").ConfigureAwait(false);
            return OperationResult<string>.Failure(new OperationError(
                "Opération annulée", "L'utilisateur a annulé l'opération."));
        }
        catch (Exception ex)
        {
            _logger.Error(operationName + " échoué", ex);
            await _audit.LogAsync(AuditActions.ComputerControl, context.ServerFqdn, AuditResult.Failure,
                operationName + " — " + ex.Message, ex.ToString()).ConfigureAwait(false);
            return OperationResult<string>.Failure(new OperationError(
                operationName + " impossible", ex.Message,
                "Consulter le fichier journal (logs/adops.log) et vérifier les droits du compte."));
        }
    }

    private async Task<OperationResult<string>> ExecutePowerAsync(
        DomainContext context,
        ComputerInfo computer,
        ManagedPowerAction action,
        CancellationToken cancellationToken)
    {
        var verb = action == ManagedPowerAction.Restart ? "redémarré" : "éteint";
        var operationName = action == ManagedPowerAction.Restart
            ? "Redémarrage de « " + computer.Name + " »"
            : "Extinction de « " + computer.Name + " »";

        if (!computer.Enabled)
        {
            return OperationResult<string>.Failure(new OperationError(
                "Compte d'ordinateur désactivé",
                "Le compte AD DS de « " + computer.Name + " » est désactivé : le poste ne peut pas être joint.",
                "Réactivez d'abord le compte depuis la console du poste."));
        }

        if (!string.IsNullOrWhiteSpace(computer.OperatingSystem)
            && computer.OperatingSystem.Contains("Server", StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<string>.Failure(new OperationError(
                "Cible serveur refusée",
                "« " + computer.Name + " » exécute " + computer.OperatingSystem + " : cette console ne pilote que des postes clients Windows.",
                "Redémarrez les serveurs depuis leur console d'administration dédiée."));
        }

        var dcHost = context.ServerFqdn.Split('.', 2)[0];
        if (string.Equals(computer.Name, dcHost, StringComparison.OrdinalIgnoreCase)
            || string.Equals(computer.Name, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<string>.Failure(new OperationError(
                "Cible protégée",
                "Le contrôleur de domaine ne peut pas être redémarré ou éteint depuis cette console.",
                "Utilisez une session d'administration dédiée au serveur."));
        }

        var command = new ControlManagedComputerCommand(computer.Name, action, 15);
        var executor = _executors.CreateRemote(computer.Name, context.Credentials);

        try
        {
            var result = await executor.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0 || result.TimedOut)
            {
                var error = _interpreter.Interpret(result, operationName);
                await AuditAsync(context, operationName, AuditResult.Failure, error.Title, result, operationName)
                    .ConfigureAwait(false);
                return OperationResult<string>.Failure(error);
            }

            var message = "Poste « " + computer.Name + " » " + verb + " (délai de 15 s). "
                + "La session WinRM se ferme pendant l'opération : le résultat est consigné dans le journal.";
            await AuditAsync(context, operationName, AuditResult.Success, message, result, operationName)
                .ConfigureAwait(false);
            _logger.Information(message);
            return OperationResult<string>.Success(message);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<string>.Failure(new OperationError(
                "Opération annulée", "L'utilisateur a annulé l'opération."));
        }
        catch (Exception ex)
        {
            _logger.Error(operationName + " échoué", ex);
            await _audit.LogAsync(AuditActions.ComputerControl, context.ServerFqdn, AuditResult.Failure,
                operationName + " — " + ex.Message, ex.ToString()).ConfigureAwait(false);
            return OperationResult<string>.Failure(new OperationError(
                operationName + " impossible", ex.Message,
                "Vérifier que le poste est en ligne et que WinRM y est activé (Enable-PSRemoting)."));
        }
    }

    private Task AuditAsync(
        DomainContext context,
        string operationName,
        AuditResult result,
        string message,
        PowerShellExecutionResult execution,
        string details)
        => _audit.LogAsync(
            AuditActions.ComputerControl,
            context.ServerFqdn,
            result,
            message,
            details + " | durée=" + execution.Duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s",
            new AuditContext(
                Server: context.ServerFqdn,
                Domain: context.DomainFqdn,
                ErrorCode: execution.ExitCode == 0 ? null : "ExitCode=" + execution.ExitCode,
                Duration: execution.Duration,
                OperationId: Guid.NewGuid().ToString("N")));
}
