using ADOps.GpoManager.Core.Commands;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Services;
using ADOps.GpoManager.PowerShell;
using ADOps.GpoManager.PowerShell.Host;

namespace ADOps.GpoManager.ActiveDirectory;

/// <summary>
/// Lecture de l'inventaire AD DS via des commandes typées (Get-ADOrganizationalUnit,
/// Get-ADComputer, Get-ADUser, Get-GPInheritance, Get-GPO). Chaque lecture est
/// indépendante : un échec partiel produit un avertissement, jamais une donnée
/// inventée. Chaque étape émet sa progression pour que l'interface affiche
/// l'avancement en direct.
/// </summary>
public sealed class ActiveDirectoryInventoryService : IActiveDirectoryInventoryService
{
    private readonly IPowerShellExecutorFactory _executors;
    private readonly AdOutputParser _parser = new();
    private readonly PowerShellOutputParser _gpoParser = new();
    private readonly IErrorInterpreter _interpreter;
    private readonly ILogger _logger;

    public ActiveDirectoryInventoryService(
        IPowerShellExecutorFactory executors,
        IErrorInterpreter interpreter,
        ILogger logger)
    {
        _executors = executors;
        _interpreter = interpreter;
        _logger = logger;
    }

    public async Task<SyncResult> ReadInventoryAsync(
        DomainContext context,
        IProgress<SyncStepResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var executor = context.ExecutorMode == ExecutorMode.Remote
            ? _executors.CreateRemote(context.ServerFqdn, context.Credentials)
            : _executors.Local;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var warnings = new List<string>();

        var ous = await ReadListAsync(
            executor, new ListOusCommand(), _parser.ParseOus, "Unités d'organisation", progress, warnings, cancellationToken).ConfigureAwait(false);

        var computers = await ReadListAsync(
            executor, new ListComputersCommand(), _parser.ParseComputers, "Ordinateurs", progress, warnings, cancellationToken).ConfigureAwait(false);

        // Les comptes d'administration sont lus AVANT les utilisateurs : la
        // classification administrateur/standard en dépend, et cette requête
        // est unique (bien plus rapide qu'un memberOf par compte).
        var administratorDns = await ReadAdministratorDnsAsync(
            executor, progress, warnings, cancellationToken).ConfigureAwait(false);

        var users = await ReadListAsync(
            executor,
            new ListUsersCommand(),
            json => _parser.ParseUsers(json, administratorDns),
            "Comptes clients",
            progress,
            warnings,
            cancellationToken).ConfigureAwait(false);

        var gpos = await ReadGposAsync(executor, progress, warnings, cancellationToken).ConfigureAwait(false);

        var links = await ReadListAsync(
            executor, new ListGpoLinksCommand(), _parser.ParseLinks, "Liens de GPO", progress, warnings, cancellationToken).ConfigureAwait(false);

        stopwatch.Stop();

        return new SyncResult
        {
            OrganizationalUnits = ous,
            Computers = computers,
            Users = users,
            Gpos = gpos,
            Links = links,
            Duration = stopwatch.Elapsed,
            Warnings = warnings,
        };
    }

    private async Task<IReadOnlyList<T>> ReadListAsync<T>(
        IPowerShellExecutor executor,
        PowerShellCommand command,
        Func<string, IReadOnlyList<T>> parse,
        string label,
        IProgress<SyncStepResult>? progress,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        progress?.Report(new SyncStepResult { Name = label, State = SyncStepState.Running });
        var stepWatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var result = await executor.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0 || result.TimedOut)
            {
                var error = _interpreter.Interpret(result, "Lecture des " + label);
                warnings.Add("Lecture des " + label + " impossible : " + error.Title);
                stepWatch.Stop();
                progress?.Report(new SyncStepResult
                {
                    Name = label,
                    State = SyncStepState.Failed,
                    Message = error.Title,
                    Duration = stepWatch.Elapsed,
                });
                return Array.Empty<T>();
            }

            var items = parse(result.StandardOutput);
            stepWatch.Stop();
            progress?.Report(new SyncStepResult
            {
                Name = label,
                State = SyncStepState.Succeeded,
                Count = items.Count,
                Message = items.Count + " élément(s) lu(s).",
                Duration = stepWatch.Elapsed,
            });
            return items;
        }
        catch (Exception ex)
        {
            _logger.Error("Erreur lecture " + label, ex);
            warnings.Add("Lecture des " + label + " impossible : " + ex.Message);
            stepWatch.Stop();
            progress?.Report(new SyncStepResult
            {
                Name = label,
                State = SyncStepState.Failed,
                Message = ex.Message,
                Duration = stepWatch.Elapsed,
            });
            return Array.Empty<T>();
        }
    }

    private async Task<IReadOnlySet<string>> ReadAdministratorDnsAsync(
        IPowerShellExecutor executor,
        IProgress<SyncStepResult>? progress,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        progress?.Report(new SyncStepResult { Name = "Comptes d'administration", State = SyncStepState.Running });
        var stepWatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var result = await executor.ExecuteAsync(new ListDomainAdministratorsCommand(), cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0 || result.TimedOut)
            {
                stepWatch.Stop();
                progress?.Report(new SyncStepResult
                {
                    Name = "Comptes d'administration",
                    State = SyncStepState.Partial,
                    Message = "Classification administrateur indisponible : tous les comptes sont classés « standard ».",
                    Duration = stepWatch.Elapsed,
                });
                warnings.Add("Classification administrateur indisponible : la liste des groupes d'administration n'a pas pu être lue.");
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var admins = _parser.ParseAdministratorDns(result.StandardOutput);
            stepWatch.Stop();
            progress?.Report(new SyncStepResult
            {
                Name = "Comptes d'administration",
                State = SyncStepState.Succeeded,
                Count = admins.Count,
                Message = admins.Count + " compte(s) d'administration identifiés.",
                Duration = stepWatch.Elapsed,
            });
            return admins;
        }
        catch (Exception ex)
        {
            _logger.Error("Erreur lecture des comptes d'administration", ex);
            stepWatch.Stop();
            progress?.Report(new SyncStepResult
            {
                Name = "Comptes d'administration",
                State = SyncStepState.Partial,
                Message = ex.Message,
                Duration = stepWatch.Elapsed,
            });
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task<IReadOnlyList<GpoSummary>> ReadGposAsync(
        IPowerShellExecutor executor,
        IProgress<SyncStepResult>? progress,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        progress?.Report(new SyncStepResult { Name = "GPO du domaine", State = SyncStepState.Running });
        var stepWatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var result = await executor.ExecuteAsync(new ListGposCommand(), cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0 || result.TimedOut)
            {
                var error = _interpreter.Interpret(result, "Liste des GPO");
                warnings.Add("Lecture des GPO impossible : " + error.Title);
                stepWatch.Stop();
                progress?.Report(new SyncStepResult
                {
                    Name = "GPO du domaine",
                    State = SyncStepState.Failed,
                    Message = error.Title,
                    Duration = stepWatch.Elapsed,
                });
                return Array.Empty<GpoSummary>();
            }

            var gpos = _gpoParser.ParseGpoList(result.StandardOutput);
            stepWatch.Stop();
            progress?.Report(new SyncStepResult
            {
                Name = "GPO du domaine",
                State = SyncStepState.Succeeded,
                Count = gpos.Count,
                Message = gpos.Count + " GPO lue(s).",
                Duration = stepWatch.Elapsed,
            });
            return gpos;
        }
        catch (Exception ex)
        {
            _logger.Error("Erreur lecture GPO", ex);
            warnings.Add("Lecture des GPO impossible : " + ex.Message);
            stepWatch.Stop();
            progress?.Report(new SyncStepResult
            {
                Name = "GPO du domaine",
                State = SyncStepState.Failed,
                Message = ex.Message,
                Duration = stepWatch.Elapsed,
            });
            return Array.Empty<GpoSummary>();
        }
    }
}
