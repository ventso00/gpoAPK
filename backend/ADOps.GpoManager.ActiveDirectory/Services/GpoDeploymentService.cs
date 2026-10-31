using System.Text.Json;
using ADOps.GpoManager.Core.Commands;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Services;
using ADOps.GpoManager.Core.Validation;
using ADOps.GpoManager.PowerShell;
using ADOps.GpoManager.PowerShell.Host;

namespace ADOps.GpoManager.ActiveDirectory;

/// <summary>
/// Moteur de déploiement des GPO :
/// - planification : validation de la sélection et rapport d'impact
///   (utilisateurs, ordinateurs et OU réellement ciblés, lus dans AD DS) ;
/// - application : New-GPLink par GPO × cible, chaque opération auditée ;
/// - suppression : Remove-GPLink.
/// Les modifications ne sont effectuées qu'à partir d'un plan validé.
/// </summary>
public sealed class GpoDeploymentService : IGpoDeploymentService
{
    private readonly IPowerShellExecutorFactory _executors;
    private readonly IErrorInterpreter _interpreter;
    private readonly IAuditService _audit;
    private readonly ILogger _logger;

    public GpoDeploymentService(
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

    /// <inheritdoc />
    public async Task<OperationResult<GpoDeploymentPlan>> PlanDeploymentAsync(
        DomainContext context,
        IReadOnlyList<GpoSummary> selectedGpos,
        IReadOnlyList<OuInfo> targetOus,
        IReadOnlyList<UserInfo> users,
        IReadOnlyList<ComputerInfo> computers,
        IReadOnlyList<GpoLinkInfo> existingLinks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var errors = new List<string>();
        var warnings = new List<string>();

        selectedGpos ??= Array.Empty<GpoSummary>();
        targetOus ??= Array.Empty<OuInfo>();
        users ??= Array.Empty<UserInfo>();
        computers ??= Array.Empty<ComputerInfo>();
        existingLinks ??= Array.Empty<GpoLinkInfo>();

        if (selectedGpos.Count == 0)
        {
            errors.Add("Aucune GPO sélectionnée. Cochez au moins une GPO dans la liste.");
        }

        if (targetOus.Count == 0)
        {
            errors.Add("Aucune unité d'organisation cible sélectionnée. Cochez au moins une OU.");
        }

        var entries = new List<GpoImpactEntry>();
        if (errors.Count == 0)
        {
            foreach (var gpo in selectedGpos)
            {
                foreach (var ou in targetOus)
                {
                    // L'impact est calculé à partir des objets réellement situés
                    // dans le sous-arbre de l'OU (DN suffixe du DN de l'OU).
                    var affectedUsers = users
                        .Where(user => IsInSubtree(user.DistinguishedName, ou.DistinguishedName))
                        .ToList();
                    var affectedComputers = computers
                        .Where(computer => IsInSubtree(computer.DistinguishedName, ou.DistinguishedName))
                        .ToList();

                    var alreadyLinked = existingLinks.Any(link
                        => link.GpoId == gpo.Id
                           && string.Equals(link.TargetDistinguishedName, ou.DistinguishedName, StringComparison.OrdinalIgnoreCase));
                    if (alreadyLinked)
                    {
                        warnings.Add("La GPO « " + gpo.DisplayName + " » est déjà liée à « " + ou.Name
                                     + " » : le lien existant sera conservé et mis à jour (New-GPLink est idempotent).");
                    }

                    entries.Add(new GpoImpactEntry
                    {
                        GpoId = gpo.Id,
                        GpoName = gpo.DisplayName,
                        TargetOuDistinguishedName = ou.DistinguishedName,
                        TargetOuName = ou.Name,
                        AffectedUsers = affectedUsers,
                        AffectedComputers = affectedComputers,
                        AlreadyLinked = alreadyLinked,
                    });
                }
            }
        }

        var plan = new GpoDeploymentPlan
        {
            Entries = entries,
            TotalAffectedUsers = entries
                .SelectMany(entry => entry.AffectedUsers)
                .Select(user => user.DistinguishedName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            TotalAffectedComputers = entries
                .SelectMany(entry => entry.AffectedComputers)
                .Select(computer => computer.DistinguishedName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Errors = errors,
        };

        await _audit.LogAsync(
            AuditActions.DeploymentPlan,
            context.ServerFqdn,
            plan.IsValid ? AuditResult.Success : AuditResult.Information,
            plan.IsValid
                ? "Plan de déploiement validé : " + plan.Entries.Count + " lien(s) prévu(s), "
                  + plan.TotalAffectedUsers + " utilisateur(s), " + plan.TotalAffectedComputers + " ordinateur(s) impacté(s)."
                : "Plan de déploiement invalide : " + string.Join(" | ", errors),
            string.Join(" | ", warnings),
            new AuditContext(
                Server: context.ServerFqdn,
                Domain: context.DomainFqdn,
                Targets: string.Join(", ", targetOus.Select(ou => ou.Name)),
                OperationId: Guid.NewGuid().ToString("N")));

        return plan.IsValid
            ? OperationResult<GpoDeploymentPlan>.Success(plan)
            : OperationResult<GpoDeploymentPlan>.Failure(new OperationError(
                "Sélection invalide",
                string.Join(" | ", errors),
                "Cochez au moins une GPO et une unité d'organisation cible."));
    }

    /// <inheritdoc />
    public async Task<OperationResult<GpoDeploymentResult>> DeployAsync(
        DomainContext context,
        GpoDeploymentPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (plan is null || !plan.IsValid)
        {
            return OperationResult<GpoDeploymentResult>.Failure(new OperationError(
                "Plan non validé",
                "Le déploiement exige un plan préalablement validé (étape de validation).",
                "Relancez la validation de la sélection avant d'appliquer les liens."));
        }

        var executor = context.ExecutorMode == ExecutorMode.Remote
            ? _executors.CreateRemote(context.ServerFqdn, context.Credentials)
            : _executors.Local;

        var operations = new List<GpoLinkOperationResult>();

        foreach (var entry in plan.Entries)
        {
            var command = new LinkGpoCommand(entry.GpoId, entry.TargetOuDistinguishedName, null, enabled: true);
            try
            {
                var result = await executor.ExecuteAsync(command, cancellationToken);
                if (result.ExitCode == 0 && !result.TimedOut)
                {
                    operations.Add(new GpoLinkOperationResult
                    {
                        GpoId = entry.GpoId,
                        GpoName = entry.GpoName,
                        TargetDistinguishedName = entry.TargetOuDistinguishedName,
                        TargetName = entry.TargetOuName,
                        IsSuccess = true,
                        Message = "Lien appliqué : « " + entry.GpoName + " » → « " + entry.TargetOuName
                                  + " » (New-GPLink). " + entry.AffectedUserCount + " utilisateur(s), "
                                  + entry.AffectedComputerCount + " ordinateur(s) ciblé(s).",
                    });

                    await _audit.LogAsync(
                        AuditActions.GpoLink,
                        context.ServerFqdn,
                        AuditResult.Success,
                        "GPO liée : " + entry.GpoName + " → " + entry.TargetOuDistinguishedName,
                        "Ciblage : " + entry.AffectedUserCount + " utilisateur(s), " + entry.AffectedComputerCount + " ordinateur(s).",
                        new AuditContext(
                            Server: context.ServerFqdn,
                            Domain: context.DomainFqdn,
                            ObjectGuid: entry.GpoId.ToString(),
                            Targets: entry.TargetOuDistinguishedName,
                            RequestedState: "Lien de GPO activé",
                            OperationId: Guid.NewGuid().ToString("N")));
                }
                else
                {
                    var error = _interpreter.Interpret(result, "Lien de GPO (New-GPLink)");
                    operations.Add(new GpoLinkOperationResult
                    {
                        GpoId = entry.GpoId,
                        GpoName = entry.GpoName,
                        TargetDistinguishedName = entry.TargetOuDistinguishedName,
                        TargetName = entry.TargetOuName,
                        IsSuccess = false,
                        Message = error.Title,
                        Error = error.ToDisplayString(),
                    });

                    await _audit.LogAsync(
                        AuditActions.GpoLink,
                        context.ServerFqdn,
                        AuditResult.Failure,
                        "Échec du lien : " + entry.GpoName + " → " + entry.TargetOuDistinguishedName,
                        result.StandardError,
                        new AuditContext(
                            Server: context.ServerFqdn,
                            Domain: context.DomainFqdn,
                            ObjectGuid: entry.GpoId.ToString(),
                            Targets: entry.TargetOuDistinguishedName,
                            ErrorCode: "ExitCode=" + result.ExitCode,
                            OperationId: Guid.NewGuid().ToString("N")));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                operations.Add(new GpoLinkOperationResult
                {
                    GpoId = entry.GpoId,
                    GpoName = entry.GpoName,
                    TargetDistinguishedName = entry.TargetOuDistinguishedName,
                    TargetName = entry.TargetOuName,
                    IsSuccess = false,
                    Message = "Opération annulée.",
                    Error = "Le déploiement a été annulé par l'utilisateur.",
                });
                break;
            }
            catch (Exception ex)
            {
                _logger.Error("Erreur New-GPLink : " + entry.GpoName, ex);
                operations.Add(new GpoLinkOperationResult
                {
                    GpoId = entry.GpoId,
                    GpoName = entry.GpoName,
                    TargetDistinguishedName = entry.TargetOuDistinguishedName,
                    TargetName = entry.TargetOuName,
                    IsSuccess = false,
                    Message = "Erreur inattendue : " + ex.Message,
                    Error = ex.ToString(),
                });
            }
        }

        var deployment = new GpoDeploymentResult
        {
            Operations = operations,
            Succeeded = operations.Count(operation => operation.IsSuccess),
            Failed = operations.Count(operation => !operation.IsSuccess),
        };

        await _audit.LogAsync(
            AuditActions.DeploymentExecute,
            context.ServerFqdn,
            deployment.IsSuccess ? AuditResult.Success : deployment.IsPartial ? AuditResult.Warning : AuditResult.Failure,
            "Déploiement terminé : " + deployment.SummaryText,
            string.Join(" | ", operations.Select(operation => operation.GpoName + " → " + operation.TargetDistinguishedName + " : " + (operation.IsSuccess ? "OK" : "ÉCHEC"))),
            new AuditContext(
                Server: context.ServerFqdn,
                Domain: context.DomainFqdn,
                OperationId: Guid.NewGuid().ToString("N")));

        return OperationResult<GpoDeploymentResult>.Success(deployment);
    }

    /// <inheritdoc />
    public async Task<OperationResult<string>> UnlinkGpoAsync(
        DomainContext context,
        Guid gpoId,
        string targetDistinguishedName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var (isValid, error) = DistinguishedNameValidator.ValidateDistinguishedName(targetDistinguishedName);
        if (!isValid)
        {
            return OperationResult<string>.Failure(new OperationError(
                "Cible invalide", error, "Fournissez un DistinguishedName d'OU ou de domaine."));
        }

        var executor = context.ExecutorMode == ExecutorMode.Remote
            ? _executors.CreateRemote(context.ServerFqdn, context.Credentials)
            : _executors.Local;
        var command = new UnlinkGpoCommand(gpoId, targetDistinguishedName);

        try
        {
            var result = await executor.ExecuteAsync(command, cancellationToken);
            if (result.ExitCode == 0 && !result.TimedOut)
            {
                var message = "Lien supprimé (Remove-GPLink) : GPO " + gpoId + " → " + targetDistinguishedName + ".";
                await _audit.LogAsync(
                    AuditActions.GpoUnlink,
                    context.ServerFqdn,
                    AuditResult.Success,
                    message,
                    null,
                    new AuditContext(
                        Server: context.ServerFqdn,
                        Domain: context.DomainFqdn,
                        ObjectGuid: gpoId.ToString(),
                        Targets: targetDistinguishedName,
                        RequestedState: "Lien de GPO supprimé",
                        OperationId: Guid.NewGuid().ToString("N")));
                _logger.Information(message);
                return OperationResult<string>.Success(message);
            }

            var interpretError = _interpreter.Interpret(result, "Suppression de lien (Remove-GPLink)");
            await _audit.LogAsync(
                AuditActions.GpoUnlink,
                context.ServerFqdn,
                AuditResult.Failure,
                interpretError.Title,
                result.StandardError,
                new AuditContext(
                    Server: context.ServerFqdn,
                    Domain: context.DomainFqdn,
                    ObjectGuid: gpoId.ToString(),
                    Targets: targetDistinguishedName,
                    ErrorCode: "ExitCode=" + result.ExitCode,
                    OperationId: Guid.NewGuid().ToString("N")));
            return OperationResult<string>.Failure(interpretError);
        }
        catch (OperationCanceledException)
        {
            await _audit.LogAsync(
                AuditActions.GpoUnlink,
                context.ServerFqdn,
                AuditResult.Warning,
                "Suppression de lien annulée par l'utilisateur.");
            return OperationResult<string>.Failure(new OperationError("Opération annulée", "L'utilisateur a annulé l'opération."));
        }
        catch (Exception ex)
        {
            _logger.Error("Erreur Remove-GPLink", ex);
            var error2 = new OperationError(
                "Erreur inattendue lors de la suppression du lien",
                ex.Message,
                "Consulter le fichier journal (logs/adops.log).");
            await _audit.LogAsync(
                AuditActions.GpoUnlink,
                context.ServerFqdn,
                AuditResult.Failure,
                error2.Title,
                ex.ToString());
            return OperationResult<string>.Failure(error2);
        }
    }

    /// <summary>
    /// Plan de déploiement exprimé en « machines et comptes clients » : les
    /// cibles sont regroupées par unité d'organisation, ce qui produit une
    /// entrée (GPO × OU) par conteneur au lieu d'une entrée par poste.
    /// </summary>
    public async Task<OperationResult<MachineDeploymentPlan>> PlanMachineDeploymentAsync(
        DomainContext context,
        IReadOnlyList<GpoSummary> selectedGpos,
        IReadOnlyList<ComputerInfo> targetComputers,
        IReadOnlyList<UserInfo> targetAccounts,
        IReadOnlyList<GpoLinkInfo> existingLinks,
        string? securityGroupDn = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        selectedGpos ??= Array.Empty<GpoSummary>();
        targetComputers ??= Array.Empty<ComputerInfo>();
        targetAccounts ??= Array.Empty<UserInfo>();
        existingLinks ??= Array.Empty<GpoLinkInfo>();

        var errors = new List<string>();
        var warnings = new List<string>();

        if (selectedGpos.Count == 0)
        {
            errors.Add("Aucune GPO sélectionnée.");
        }

        if (targetComputers.Count == 0 && targetAccounts.Count == 0)
        {
            errors.Add("Aucun poste ni compte client sélectionné.");
        }

        if (errors.Count > 0)
        {
            return OperationResult<MachineDeploymentPlan>.Failure(new OperationError(
                "Sélection incomplète",
                string.Join(" | ", errors),
                "Sélectionnez au moins une GPO et au moins un poste ou compte client."));
        }

        // Regroupement par unité d'organisation : le point d'ancrage du lien
        // est l'OU qui contient les cibles.
        var computerGroups = targetComputers
            .Where(computer => !string.IsNullOrWhiteSpace(computer.OuDistinguishedName))
            .GroupBy(computer => computer.OuDistinguishedName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var computersWithoutOu = targetComputers
            .Where(computer => string.IsNullOrWhiteSpace(computer.OuDistinguishedName))
            .ToList();

        if (computersWithoutOu.Count > 0)
        {
            warnings.Add(computersWithoutOu.Count
                + " poste(s) n'ont pas d'unité d'organisation identifiable : ils ne sont pas ciblés.");
        }

        var accountGroups = targetAccounts
            .Where(user => !string.IsNullOrWhiteSpace(user.OuDistinguishedName))
            .GroupBy(user => user.OuDistinguishedName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var ouNames = await ResolveOuNamesAsync(context, computerGroups.Select(g => g.Key), cancellationToken)
            .ConfigureAwait(false);

        var entries = new List<MachineDeploymentEntry>();
        var containers = computerGroups.Select(group => group.Key)
            .Concat(accountGroups.Select(group => group.Key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var gpo in selectedGpos)
        {
            foreach (var container in containers)
            {
                var computersInContainer = computerGroups
                    .FirstOrDefault(group => string.Equals(group.Key, container, StringComparison.OrdinalIgnoreCase))
                    ?.Select(computer => computer.Name)
                    .ToList() ?? new List<string>();

                var accountsInContainer = accountGroups
                    .FirstOrDefault(group => string.Equals(group.Key, container, StringComparison.OrdinalIgnoreCase))
                    ?.Select(user => user.SamAccountName)
                    .ToList() ?? new List<string>();

                if (computersInContainer.Count == 0 && accountsInContainer.Count == 0)
                {
                    continue;
                }

                var alreadyLinked = existingLinks.Any(link =>
                    link.GpoId == gpo.Id
                    && string.Equals(link.TargetDistinguishedName, container, StringComparison.OrdinalIgnoreCase));

                if (alreadyLinked)
                {
                    warnings.Add("La GPO « " + gpo.DisplayName + " » est déjà liée à « "
                        + (ouNames.TryGetValue(container, out var name) ? name : container)
                        + " » : le lien existant sera mis à jour.");
                }

                entries.Add(new MachineDeploymentEntry
                {
                    GpoId = gpo.Id,
                    GpoName = gpo.DisplayName,
                    TargetDistinguishedName = container,
                    TargetName = ouNames.TryGetValue(container, out var label) ? label : container,
                    ComputerNames = computersInContainer,
                    AccountNames = accountsInContainer,
                    AlreadyLinked = alreadyLinked,
                });
            }
        }

        var plan = new MachineDeploymentPlan
        {
            Entries = entries,
            TargetAccounts = targetAccounts.ToList(),
            TargetComputers = targetComputers.ToList(),
            SecurityGroupDn = string.IsNullOrWhiteSpace(securityGroupDn) ? null : securityGroupDn.Trim(),
            Warnings = warnings,
            Errors = errors,
        };

        await _audit.LogAsync(
            AuditActions.DeploymentPlan,
            context.ServerFqdn,
            AuditResult.Success,
            "Plan (machines) : " + entries.Count + " lien(s) prévu(s), "
                + plan.AffectedComputerCount + " poste(s), "
                + plan.AffectedAccountCount + " compte(s) client.",
            string.Join(" | ", warnings),
            new AuditContext(
                Server: context.ServerFqdn,
                Domain: context.DomainFqdn,
                Targets: string.Join(", ", containers),
                RequestedState: "Plan de déploiement par machines",
                OperationId: Guid.NewGuid().ToString("N"))).ConfigureAwait(false);

        return OperationResult<MachineDeploymentPlan>.Success(plan);
    }

    /// <summary>
    /// Applique un plan « machines » : liens, filtre de sécurité,
    /// rafraîchissement et vérification des postes cibles.
    /// </summary>
    public async Task<OperationResult<MachineDeploymentResult>> DeployToMachinesAsync(
        DomainContext context,
        MachineDeploymentPlan plan,
        bool applySecurityFilter,
        bool refreshTargets,
        IProgress<ExecutionLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (plan is null || !plan.IsValid)
        {
            return OperationResult<MachineDeploymentResult>.Failure(new OperationError(
                "Plan non validé",
                "Le déploiement exige un plan préalablement validé.",
                "Revenez à l'étape de vérification et validez le plan."));
        }

        var executor = context.ExecutorMode == ExecutorMode.Remote
            ? _executors.CreateRemote(context.ServerFqdn, context.Credentials)
            : _executors.Local;

        var operations = new List<GpoLinkOperationResult>();
        var refreshed = new List<string>();
        var failedRefreshes = new List<string>();

        progress?.Report(ExecutionLine.Command("New-GPLink — création des liens de GPO"));

        foreach (var entry in plan.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(ExecutionLine.Info(
                "Lien : « " + entry.GpoName + " » → « " + entry.TargetName + " » ("
                + entry.ComputerNames.Count + " poste(s), " + entry.AccountNames.Count + " compte(s))"));

            var command = new LinkGpoCommand(entry.GpoId, entry.TargetDistinguishedName, null, enabled: true);
            var result = await executor.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);

            if (result.ExitCode == 0 && !result.TimedOut)
            {
                operations.Add(new GpoLinkOperationResult
                {
                    GpoId = entry.GpoId,
                    GpoName = entry.GpoName,
                    TargetDistinguishedName = entry.TargetDistinguishedName,
                    TargetName = entry.TargetName,
                    IsSuccess = true,
                    Message = "Lien appliqué sur « " + entry.TargetName + " ».",
                });

                progress?.Report(ExecutionLine.Success("Lien appliqué : « " + entry.GpoName + " » → « " + entry.TargetName + " »"));
            }
            else
            {
                var error = _interpreter.Interpret(result, "Lien de GPO (New-GPLink)");
                operations.Add(new GpoLinkOperationResult
                {
                    GpoId = entry.GpoId,
                    GpoName = entry.GpoName,
                    TargetDistinguishedName = entry.TargetDistinguishedName,
                    TargetName = entry.TargetName,
                    IsSuccess = false,
                    Message = error.Title,
                    Error = error.ToDisplayString(),
                });

                progress?.Report(ExecutionLine.Error("Lien en échec : « " + entry.GpoName + " » → « " + entry.TargetName + " » — " + error.Title));
            }
        }

        // Filtre de sécurité : n'autorise la GPO qu'au groupe retenu et
        // écarte les comptes d'administration.
        if (applySecurityFilter && !string.IsNullOrWhiteSpace(plan.SecurityGroupDn))
        {
            progress?.Report(ExecutionLine.Command("Set-GPPermission — filtre de sécurité"));

            // Récupère le DN du groupe Domain Admins pour l'exclure du filtre
            var domainAdminsDn = await GetDomainAdminsDnAsync(context, executor, cancellationToken).ConfigureAwait(false);

            foreach (var gpoId in plan.Entries.Select(entry => entry.GpoId).Distinct())
            {
                var filter = new SetGpoSecurityFilterCommand(gpoId, plan.SecurityGroupDn, domainAdminsDn);
                var filterResult = await executor.ExecuteAsync(filter, cancellationToken).ConfigureAwait(false);

                if (filterResult.ExitCode == 0 && !filterResult.TimedOut)
                {
                    progress?.Report(ExecutionLine.Success("Filtre de sécurité appliqué (groupe « " + plan.SecurityGroupDn + " », administrateurs exclus)"));
                }
                else
                {
                    var error = _interpreter.Interpret(filterResult, "Filtre de sécurité (Set-GPPermission)");
                    progress?.Report(ExecutionLine.Error("Filtre de sécurité en échec : " + error.Title));
                }
            }
        }

        // Rafraîchissement et vérification des postes cibles.
        if (refreshTargets && plan.TargetComputers.Count > 0)
        {
            progress?.Report(ExecutionLine.Command("gpupdate /force — rafraîchissement des postes cibles"));

            foreach (var computer in plan.TargetComputers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(ExecutionLine.Info("Actualisation de « " + computer.Name + " »…"));

                var outcome = await RefreshComputerAsync(context, computer.Name, cancellationToken).ConfigureAwait(false);
                if (outcome.Refreshed)
                {
                    refreshed.Add(computer.Name);
                    progress?.Report(ExecutionLine.Success("« " + computer.Name + " » actualisé"
                        + (outcome.AppliedGpos.Count > 0 ? " — GPO appliquées : " + string.Join(", ", outcome.AppliedGpos) : string.Empty)));
                }
                else
                {
                    failedRefreshes.Add(computer.Name);
                    progress?.Report(ExecutionLine.Warning("« " + computer.Name + " » non actualisé : " + outcome.Error));
                }
            }
        }

        var deployment = new MachineDeploymentResult
        {
            Operations = operations,
            RefreshedComputers = refreshed,
            FailedRefreshes = failedRefreshes,
            AppliedAccounts = plan.TargetAccounts.ToList(),
        };

        await _audit.LogAsync(
            AuditActions.DeploymentExecute,
            context.ServerFqdn,
            deployment.IsSuccess ? AuditResult.Success : AuditResult.Warning,
            "Déploiement (machines) terminé : " + deployment.SummaryText,
            string.Join(" | ", operations.Select(operation =>
                operation.GpoName + " → " + operation.TargetName + " : " + (operation.IsSuccess ? "OK" : "ÉCHEC"))),
            new AuditContext(
                Server: context.ServerFqdn,
                Domain: context.DomainFqdn,
                Targets: string.Join(", ", plan.TargetComputers.Select(computer => computer.Name)),
                RequestedState: "Application de GPO sur machines et comptes clients",
                OperationId: Guid.NewGuid().ToString("N"))).ConfigureAwait(false);

        return OperationResult<MachineDeploymentResult>.Success(deployment);
    }

    /// <summary>Actualise un poste client et relit les GPO qui lui sont appliquées.</summary>
    private async Task<(bool Refreshed, IReadOnlyList<string> AppliedGpos, string Error)> RefreshComputerAsync(
        DomainContext context,
        string computerName,
        CancellationToken cancellationToken)
    {
        try
        {
            // La commande s'exécute sur le poste cible : l'exécuteur distant
            // est donc obligatoire, quel que soit le mode de la session.
            var remote = _executors.CreateRemote(computerName, context.Credentials);
            var update = await remote.ExecuteAsync(new InvokeGpUpdateCommand(computerName), cancellationToken).ConfigureAwait(false);

            // Un JSON « Computer » sur stdout prouve l'exécution distante : un code
            // de sortie 1 venu du seul transport (erreur sérialisée relayée) ne doit
            // pas faire perdre le résultat.
            var updateOk = !update.TimedOut
                && (update.ExitCode == 0 || update.StandardOutput.Contains("\"Computer\"", StringComparison.Ordinal));
            if (!updateOk)
            {
                var error = _interpreter.Interpret(update, "Actualisation (gpupdate)");
                return (false, Array.Empty<string>(), error.Title);
            }

            var gpos = await remote.ExecuteAsync(new GetGpResultCommand(computerName), cancellationToken).ConfigureAwait(false);
            var names = !gpos.TimedOut
                ? ParseAppliedGpoNames(gpos.StandardOutput)
                : Array.Empty<string>();

            return (true, names, string.Empty);
        }
        catch (Exception ex)
        {
            _logger.Warning("Actualisation impossible sur " + computerName + " : " + ex.Message);
            return (false, Array.Empty<string>(), ex.Message);
        }
    }

    /// <summary>Extrait les noms de GPO de la sortie JSON de gpresult.</summary>
    private static IReadOnlyList<string> ParseAppliedGpoNames(string json)
        => GpResultReportParser.FromCommandOutput(json);

    /// <summary>Extrait un tableau de chaînes depuis une sortie JSON.</summary>
    private static IReadOnlyList<string> ParseStringArray(string json, string propertyName)
    {
        var values = new List<string>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return values;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty(propertyName, out var element))
            {
                if (element.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in element.EnumerateArray())
                    {
                        var value = item.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            values.Add(value);
                        }
                    }
                }
                else if (element.ValueKind == JsonValueKind.String)
                {
                    var value = element.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        values.Add(value);
                    }
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Sortie illisible : liste vide.
        }

        return values;
    }

    /// <summary>Résout les noms lisibles des unités d'organisation ciblées.</summary>
    private async Task<IReadOnlyDictionary<string, string>> ResolveOuNamesAsync(
        DomainContext context,
        IEnumerable<string> distinguishedNames,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var names = distinguishedNames
            .Where(dn => !string.IsNullOrWhiteSpace(dn))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (names.Count == 0)
        {
            return result;
        }

        try
        {
            var executor = context.ExecutorMode == ExecutorMode.Remote
                ? _executors.CreateRemote(context.ServerFqdn, context.Credentials)
                : _executors.Local;

            var execution = await executor.ExecuteAsync(new ListOusCommand(), cancellationToken).ConfigureAwait(false);
            if (execution.ExitCode != 0 || execution.TimedOut)
            {
                return result;
            }

            var ous = new AdOutputParser().ParseOus(execution.StandardOutput);
            foreach (var ou in ous)
            {
                if (names.Contains(ou.DistinguishedName, StringComparer.OrdinalIgnoreCase))
                {
                    result[ou.DistinguishedName] = ou.Name;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning("Résolution des noms d'OU impossible : " + ex.Message);
        }

        return result;
    }

    /// <summary>Crée une GPO dans le domaine (option « Ajouter une GPO »).</summary>
    public async Task<OperationResult<GpoSummary>> CreateGpoAsync(
        DomainContext context,
        string gpoName,
        IReadOnlyList<GpoRegistrySetting> settings,
        string? comment = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        GpoSummary created;
        try
        {
            var command = new CreateGpoCommand(gpoName, settings, comment);
            var executor = context.ExecutorMode == ExecutorMode.Remote
                ? _executors.CreateRemote(context.ServerFqdn, context.Credentials)
                : _executors.Local;

            var result = await executor.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0 || result.TimedOut)
            {
                var error = _interpreter.Interpret(result, "Création de GPO (New-GPO)");
                await _audit.LogAsync(
                    AuditActions.GpoCreate,
                    context.ServerFqdn,
                    AuditResult.Failure,
                    error.Title,
                    result.StandardError,
                    new AuditContext(
                        Server: context.ServerFqdn,
                        Domain: context.DomainFqdn,
                        OperationId: Guid.NewGuid().ToString("N"))).ConfigureAwait(false);

                return OperationResult<GpoSummary>.Failure(error);
            }

            created = new GpoSummary
            {
                Id = ParseCreatedGpoId(result.StandardOutput),
                DisplayName = gpoName.Trim(),
                Description = comment,
                Status = GpoStatus.AllSettingsEnabled,
                DomainName = context.DomainFqdn,
            };
        }
        catch (ArgumentException ex)
        {
            return OperationResult<GpoSummary>.Failure(new OperationError(
                "Paramètres invalides",
                ex.Message,
                "Corrigez les paramètres de la GPO."));
        }
        catch (Exception ex)
        {
            _logger.Error("Création de GPO échouée", ex);
            return OperationResult<GpoSummary>.Failure(new OperationError(
                "Erreur inattendue lors de la création de la GPO",
                ex.Message,
                "Consultez le journal (logs/adops.log)."));
        }

        await _audit.LogAsync(
            AuditActions.GpoCreate,
            created.Id.ToString(),
            AuditResult.Success,
            "GPO créée : « " + created.DisplayName + " » (" + settings.Count + " paramètre(s)).",
            string.Join(" | ", settings.Select(setting => setting.Key + "\\" + setting.ValueName + " = " + setting.Value)),
            new AuditContext(
                Server: context.ServerFqdn,
                Domain: context.DomainFqdn,
                ObjectGuid: created.Id.ToString(),
                OperationId: Guid.NewGuid().ToString("N"))).ConfigureAwait(false);

        return OperationResult<GpoSummary>.Success(created);
    }

    private static Guid ParseCreatedGpoId(string json)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("GpoId", out var element))
            {
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value) && Guid.TryParse(value, out var guid))
                {
                    return guid;
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Sortie illisible : identifiant vide.
        }

        return Guid.Empty;
    }

    /// <summary>
    /// Un objet (utilisateur, ordinateur) appartient au sous-arbre d'un conteneur
    /// si son DistinguishedName se termine par le DN du conteneur.
    /// </summary>
    internal static bool IsInSubtree(string objectDistinguishedName, string containerDistinguishedName)
        => objectDistinguishedName.EndsWith(
            "," + containerDistinguishedName,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Récupère le DistinguishedName du groupe Domain Admins du domaine courant.
    /// Utilisé pour exclure les administrateurs du filtrage de sécurité des GPO.
    /// </summary>
    private async Task<string?> GetDomainAdminsDnAsync(
        DomainContext context,
        IPowerShellExecutor executor,
        CancellationToken cancellationToken)
    {
        try
        {
            var command = new GetAdGroupMemberCommand("CN=Domain Admins," + context.DomainFqdn.Split('.').Select(p => "DC=" + p).Aggregate((a, b) => a + "," + b));
            var result = await executor.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0 || result.TimedOut)
            {
                _logger.Warning("Impossible de récupérer Domain Admins : " + result.StandardError);
                return null;
            }

            var names = ParseStringArray(result.StandardOutput, "DistinguishedName");
            return names.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.Warning("Récupération de Domain Admins échouée : " + ex.Message);
            return null;
        }
    }
}
