using System.Globalization;
using System.Text.Json;
using ADOps.GpoManager.Core.Commands;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Services;
using ADOps.GpoManager.PowerShell;
using ADOps.GpoManager.PowerShell.Host;

namespace ADOps.GpoManager.ActiveDirectory;

/// <summary>
/// Déploiement réel des modèles du catalogue : matérialisation des valeurs de
/// registre dans une GPO du domaine, ciblage par groupe de sécurité AD DS,
/// liaison aux OU, sans exiger WinRM sur les postes clients.
/// </summary>
public sealed class GpoTemplateDeploymentService : IGpoTemplateDeploymentService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Clé de la stratégie « Mode de traitement en boucle ».</summary>
    private const string LoopbackKey = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\System";

    private readonly IGpoCatalogService _catalog;
    private readonly IPowerShellExecutorFactory _executors;
    private readonly IErrorInterpreter _interpreter;
    private readonly IAuditService _audit;
    private readonly ILogger _logger;

    public GpoTemplateDeploymentService(
        IGpoCatalogService catalog,
        IPowerShellExecutorFactory executors,
        IErrorInterpreter interpreter,
        IAuditService audit,
        ILogger logger)
    {
        _catalog = catalog;
        _executors = executors;
        _interpreter = interpreter;
        _audit = audit;
        _logger = logger;
    }

    public async Task<OperationResult<TemplateDeploymentResult>> DeployAsync(
        DomainContext context,
        IReadOnlyList<TemplateDeploymentSelection> selections,
        IReadOnlyList<UserInfo> users,
        bool applySecurityFiltering,
        IProgress<ExecutionLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var started = DateTimeOffset.UtcNow;

        if (selections is null || selections.Count == 0)
        {
            return Failure("Aucun modèle sélectionné", "Le déploiement exige au moins un modèle du catalogue.", "Cochez une ou plusieurs GPO dans la page « GPO à appliquer ».");
        }

        users ??= Array.Empty<UserInfo>();

        var executor = context.ExecutorMode == ExecutorMode.Remote
            ? _executors.CreateRemote(context.ServerFqdn, context.Credentials)
            : _executors.Local;

        // Récupère toutes les OU du domaine pour la liaison des GPOs.
        // L'exécuteur est cohérent avec le mode d'exécution (Local/RSAT ou Distant/WinRM vers le DC).
        var allOus = await GetAllOusAsync(executor, cancellationToken).ConfigureAwait(false);
        if (allOus.Count == 0)
        {
            progress?.Report(ExecutionLine.Warning("Aucune OU détectée dans le domaine : la liaison des GPO sera limitée à la racine du domaine."));
        }

        var outcomes = new List<TemplateDeploymentOutcome>();
        var deployments = new List<GpoDeploymentRecord>();

        progress?.Report(ExecutionLine.Command("ADOps — matérialisation des modèles sélectionnés"));

        foreach (var selection in selections)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var template = _catalog.GetById(selection.TemplateId);
            if (template is null)
            {
                outcomes.Add(Failed(selection.TemplateId, "Modèle introuvable dans le catalogue."));
                progress?.Report(ExecutionLine.Error("Modèle introuvable : " + selection.TemplateId));
                continue;
            }

            var binding = _catalog.GetBinding(selection.TemplateId);
            if (binding is null)
            {
                outcomes.Add(Failed(selection.TemplateId, "Ce modèle n'a pas de liaison de registre : il n'est pas déployable."));
                progress?.Report(ExecutionLine.Error("Modèle non déployable : " + template.DisplayName));
                continue;
            }

            progress?.Report(ExecutionLine.Info("Matérialisation de « " + template.DisplayName + " »…"));

            IReadOnlyList<GpoRegistrySetting> settings;
            try
            {
                settings = _catalog.Materialize(selection.TemplateId, selection.Parameters);
            }
            catch (ArgumentException ex)
            {
                outcomes.Add(Failed(selection.TemplateId, ex.Message));
                progress?.Report(ExecutionLine.Error("« " + template.DisplayName + " » — " + ex.Message));
                continue;
            }

            // Traitement en boucle (Loopback) en mode « Merge » pour toute GPO de
            // portée Ordinateur : la configuration ordinateur est bien appliquée
            // sur le poste client où l'utilisateur ciblé ouvre une session.
            // Registre : HKLM\Software\Policies\Microsoft\Windows\System
            //            UserPolicyMode = 1 (1 = Merge, 2 = Replace)
            if (template.Scope == PolicyScope.Computer)
            {
                settings = WithLoopbackMerge(settings);
                progress?.Report(ExecutionLine.Info(
                    "Traitement en boucle activé (mode Merge) pour « " + template.DisplayName + " »."));
            }

            progress?.Report(ExecutionLine.Info(settings.Count + " valeur(s) de registre préparée(s)."));

            var gpoName = string.IsNullOrWhiteSpace(selection.GpoName)
                ? BuildGpoName(template.DisplayName)
                : Trim(selection.GpoName.Trim(), 120);
            var comment = TemplateMaterializer.BuildComment(template, TemplateMaterializer.MergeDefaults(binding, selection.Parameters));

            Guid gpoId;
            try
            {
                var apply = await executor.ExecuteAsync(
                    new ApplyGpoTemplateCommand(gpoName, settings, comment), cancellationToken).ConfigureAwait(false);

                if (apply.ExitCode != 0 || apply.TimedOut)
                {
                    var error = _interpreter.Interpret(apply, "Création / mise à jour de la GPO (" + template.DisplayName + ")");
                    outcomes.Add(Failed(selection.TemplateId, error.ToDisplayString(), gpoName));
                    progress?.Report(ExecutionLine.Error("« " + template.DisplayName + " » : " + error.Title));
                    continue;
                }

                gpoId = ParseGuid(apply.StandardOutput, "GpoId");
                if (gpoId == Guid.Empty)
                {
                    outcomes.Add(Failed(selection.TemplateId, "La GPO a été créée mais son identifiant n'a pas pu être relu.", gpoName));
                    progress?.Report(ExecutionLine.Error("Identifiant de GPO illisible pour « " + template.DisplayName + " »."));
                    continue;
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Matérialisation du modèle " + selection.TemplateId + " échouée", ex);
                outcomes.Add(Failed(selection.TemplateId, ex.Message, gpoName));
                progress?.Report(ExecutionLine.Error("« " + template.DisplayName + " » : " + ex.Message));
                continue;
            }

            progress?.Report(ExecutionLine.Success("GPO « " + gpoName + " » matérialisée (" + settings.Count + " valeur(s))."));

            // Détermine les cibles réelles selon la portée.
            string? filterGroup = null;
            IReadOnlyList<string> linkTargets;
            IReadOnlyList<string> memberDns;
            var domainRoot = BuildDomainDistinguishedName(context.DomainFqdn);

            var targetUsers = users
                .Where(user => user.Enabled && !user.IsAdministrator && !string.IsNullOrWhiteSpace(user.DistinguishedName))
                .GroupBy(user => user.DistinguishedName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            if (template.Scope == PolicyScope.Computer)
            {
                // Portée Ordinateur : la configuration ordinateur ne peut être
                // portée par un compte utilisateur. On lie donc la GPO aux OU des
                // postes clients du domaine et le filtrage accorde GpoApply à
                // « Domain Computers ». Le traitement en boucle (Merge) garantit
                // en plus l'application de la stratégie sur le poste où l'un des
                // utilisateurs ciblés ouvre une session.
                var clientComputers = await GetClientComputersAsync(executor, cancellationToken).ConfigureAwait(false);
                linkTargets = clientComputers
                    .Select(computer => computer.OuDistinguishedName)
                    .Where(dn => !string.IsNullOrWhiteSpace(dn))
                    .Select(dn => dn!.Trim())
                    // New-GPLink ne peut pas cibler un conteneur (CN=Computers…).
                    .Where(dn => dn.StartsWith("OU=", StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (linkTargets.Count == 0)
                {
                    linkTargets = new List<string> { domainRoot };
                    progress?.Report(ExecutionLine.Warning(
                        "Aucun poste client avec OU identifiable : la GPO sera liée à la racine du domaine "
                        + "et filtrée sur « Domain Computers »."));
                }
                else
                {
                    progress?.Report(ExecutionLine.Info(
                        "Ciblage hybride : " + clientComputers.Count + " poste(s) client(s) découvert(s) dans "
                        + linkTargets.Count + " OU(s)."));
                }

                // Les utilisateurs ciblés sont membres du groupe de filtrage et
                // reçoivent GpoRead (nécessaire au traitement en boucle).
                memberDns = targetUsers.Select(user => user.DistinguishedName).ToList();
            }
            else
            {
                // Pour les GPO utilisateur : cible les utilisateurs standards (non-admin, activés)
                if (targetUsers.Count == 0)
                {
                    outcomes.Add(Failed(selection.TemplateId,
                        "Stratégie de portée Utilisateur : aucun compte standard ciblé. "
                        + "Sélectionnez des utilisateurs standards dans l'étape 1.", gpoName));
                    progress?.Report(ExecutionLine.Error("« " + template.DisplayName + " » : aucun compte utilisateur cible."));
                    continue;
                }

                // Détermine les cibles de liaison à partir des OU/DN des utilisateurs sélectionnés.
                // Si l'utilisateur est dans un conteneur (CN=Users), on liera la GPO à la racine du domaine
                // et le filtrage de sécurité (groupe) restreindra l'application aux seuls membres ciblés.
                linkTargets = targetUsers
                    .Select(user =>
                    {
                        if (string.IsNullOrWhiteSpace(user.OuDistinguishedName)) return null;
                        var dn = user.OuDistinguishedName!.Trim();
                        // Si le DN commence par CN= (conteneur), on utilise la racine du domaine
                        // car New-GPLink ne peut pas lier une GPO à un conteneur.
                        if (dn.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                        {
                            progress?.Report(ExecutionLine.Warning(
                                "Utilisateur " + user.SamAccountName + " dans le conteneur " + dn +
                                " : liaison de la GPO à la racine du domaine (les conteneurs ne supportent pas les liens GPO)."));
                            return domainRoot;
                        }
                        return dn;
                    })
                    .Where(dn => !string.IsNullOrWhiteSpace(dn))
                    .Select(dn => dn!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Si malgré tout il n'y a pas de cible valide, on utilise la racine du domaine
                if (linkTargets.Count == 0)
                {
                    linkTargets = new List<string> { domainRoot };
                    progress?.Report(ExecutionLine.Warning("Aucune OU utilisateur détectée : la GPO sera liée à la racine du domaine et filtrée par groupe de sécurité."));
                }

                memberDns = targetUsers.Select(user => user.DistinguishedName).ToList();
                progress?.Report(ExecutionLine.Info("Portée Utilisateur : " + memberDns.Count + " compte(s) standard ciblé(s) dans " + linkTargets.Count + " cible(s)."));
            }

            // Application du filtre de sécurité si demandé
            if (applySecurityFiltering)
            {
                // Une GPO de portée Ordinateur reste déployable sans compte cible :
                // « Domain Computers » assure alors l'application.
                if (memberDns.Count == 0 && template.Scope != PolicyScope.Computer)
                {
                    outcomes.Add(Failed(selection.TemplateId, "Aucun membre pour le filtre de sécurité.", gpoName));
                    progress?.Report(ExecutionLine.Error("Filtrage impossible : aucun membre cible pour « " + gpoName + " »."));
                    continue;
                }

                try
                {
                    filterGroup = BuildGroupName(template.TemplateId);
                    var targeting = await executor.ExecuteAsync(
                        new SetGpoTargetingCommand(
                            gpoId,
                            filterGroup,
                            "ADOps — ciblage de « " + Trim(template.DisplayName, 80) + " »",
                            memberDns,
                            applyToComputers: template.Scope == PolicyScope.Computer),
                        cancellationToken).ConfigureAwait(false);

                    if (targeting.ExitCode != 0 || targeting.TimedOut)
                    {
                        var error = _interpreter.Interpret(targeting, "Filtrage de sécurité (Set-GPPermission)");
                        outcomes.Add(Failed(selection.TemplateId,
                            "Filtrage de sécurité en échec ; la GPO n'a pas été liée pour ne pas s'appliquer à tout le domaine. " + error.ToDisplayString(),
                            gpoName));
                        progress?.Report(ExecutionLine.Error("Filtrage en échec pour « " + gpoName + " » : " + error.Title));
                        continue;
                    }

                    progress?.Report(ExecutionLine.Success(template.Scope == PolicyScope.Computer
                        ? "Filtrage AD DS : groupe « " + filterGroup + " » (" + memberDns.Count
                          + " compte(s) en lecture) — GpoApply accordé à « Domain Computers » pour les postes clients."
                        : "Filtrage AD DS : groupe « " + filterGroup + " » (" + memberDns.Count
                          + " membre(s)) — administrateurs exclus par construction ; "
                          + "« Domain Computers » conserve la lecture."));
                }
                catch (Exception ex)
                {
                    _logger.Error("Filtrage de la GPO " + gpoName + " échoué", ex);
                    outcomes.Add(Failed(selection.TemplateId, "Filtrage impossible : " + ex.Message, gpoName));
                    progress?.Report(ExecutionLine.Error("Filtrage impossible pour « " + gpoName + " » : " + ex.Message));
                    continue;
                }
            }
            else
            {
                progress?.Report(ExecutionLine.Warning(
                    "Filtrage désactivé : « " + gpoName + " » s'appliquera à tous les objets des OU liées."));
            }

            // Liaison de la GPO aux cibles déterminées
            var appliedTargets = new List<string>();
            foreach (var target in linkTargets)
            {
                try
                {
                    var link = await executor.ExecuteAsync(new LinkGpoCommand(gpoId, target), cancellationToken).ConfigureAwait(false);
                    if (link.ExitCode == 0 && !link.TimedOut)
                    {
                        appliedTargets.Add(target);
                        progress?.Report(ExecutionLine.Success("Lien « " + gpoName + " » → " + DisplayOu(target) + " "));
                    }
                    else
                    {
                        var error = _interpreter.Interpret(link, "Liaison de GPO (New-GPLink)");
                        progress?.Report(ExecutionLine.Error("Lien en échec sur " + DisplayOu(target) + " : " + error.Title));
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning("Lien GPO " + gpoName + " → " + target + " impossible : " + ex.Message);
                    progress?.Report(ExecutionLine.Error("Lien en échec sur " + DisplayOu(target) + " : " + ex.Message));
                }
            }

            if (appliedTargets.Count == 0)
            {
                outcomes.Add(Failed(selection.TemplateId, "Aucun lien de GPO n'a pu être créé.", gpoName));
                continue;
            }

            var outcome = new TemplateDeploymentOutcome
            {
                TemplateId = selection.TemplateId,
                GpoId = gpoId,
                GpoName = gpoName,
                SettingsCount = settings.Count,
                LinkTargets = appliedTargets,
                FilterGroup = filterGroup,
                IsSuccess = true,
            };
            outcomes.Add(outcome);

            var settingsJson = JsonSerializer.Serialize(
                settings.Select(setting => new { key = setting.Key, name = setting.ValueName, type = setting.ValueType, value = setting.Value }),
                JsonOptions);

            if (template.Scope == PolicyScope.User)
            {
                foreach (var user in targetUsers)
                {
                    deployments.Add(new GpoDeploymentRecord
                    {
                        GpoId = gpoId,
                        GpoName = gpoName,
                        TemplateId = selection.TemplateId,
                        ComputerName = user.SamAccountName,
                        ComputerDistinguishedName = user.DistinguishedName,
                        OuDistinguishedName = user.OuDistinguishedName ?? string.Empty,
                        FilterGroup = filterGroup,
                        SettingsJson = settingsJson,
                        DeployedUtc = DateTimeOffset.UtcNow,
                    });
                }
            }
            else
            {
                // Portée Ordinateur : une trace par OU de postes liée, afin que
                // l'interface affiche les GPO réellement matérialisées et liées.
                foreach (var target in appliedTargets)
                {
                    deployments.Add(new GpoDeploymentRecord
                    {
                        GpoId = gpoId,
                        GpoName = gpoName,
                        TemplateId = selection.TemplateId,
                        ComputerName = "(postes clients)",
                        ComputerDistinguishedName = target,
                        OuDistinguishedName = target,
                        FilterGroup = filterGroup,
                        SettingsJson = settingsJson,
                        DeployedUtc = DateTimeOffset.UtcNow,
                    });
                }
            }

            await _audit.LogAsync(
                AuditActions.GpoTemplateApply,
                gpoId.ToString(),
                AuditResult.Success,
                "GPO de modèle appliquée : « " + gpoName + " » (" + settings.Count + " valeur(s)).",
                "Modèle : " + selection.TemplateId + " | OU/Lien : " + string.Join(", ", appliedTargets)
                + " | Groupe : " + (filterGroup ?? "(aucun)"),
                new AuditContext(
                    Server: context.ServerFqdn,
                    Domain: context.DomainFqdn,
                    ObjectGuid: gpoId.ToString(),
                    Targets: template.Scope == PolicyScope.User
                        ? string.Join(", ", users.Where(u => u.Enabled && !u.IsAdministrator).Select(u => u.SamAccountName))
                        : "(portée ordinateur)",
                    RequestedState: "Application du modèle " + selection.TemplateId,
                    OperationId: Guid.NewGuid().ToString("N"))).ConfigureAwait(false);
        }

        var successfulNames = outcomes.Where(outcome => outcome.IsSuccess).Select(outcome => outcome.GpoName).ToList();

        var result = new TemplateDeploymentResult
        {
            Outcomes = outcomes,
            Records = deployments,
            RefreshedComputers = new List<string>(),
            FailedComputers = new List<string>(),
            StartedUtc = started,
            CompletedUtc = DateTimeOffset.UtcNow,
        };

        if (outcomes.All(outcome => !outcome.IsSuccess))
        {
            return OperationResult<TemplateDeploymentResult>.Failure(new OperationError(
                "Déploiement échoué",
                "Aucune GPO n'a pu être matérialisée et liée.",
                "Consultez le terminal d'exécution et le journal d'audit pour le détail par opération."));
        }

        await _audit.LogAsync(
            AuditActions.DeploymentExecute,
            context.ServerFqdn,
            AuditResult.Success,
            "Déploiement par modèles terminé : " + result.SummaryText,
            string.Join(" | ", outcomes.Select(outcome => outcome.TemplateId + " : "
                + (outcome.IsSuccess ? "OK (" + outcome.SettingsCount + " valeurs, " + outcome.LinkTargets.Count + " lien)" : "ÉCHEC — " + outcome.Error))),
            new AuditContext(
                Server: context.ServerFqdn,
                Domain: context.DomainFqdn,
                Targets: string.Join(", ", outcomes.Where(o => o.IsSuccess).Select(o => o.GpoName)),
                RequestedState: "Application réelle des modèles du catalogue",
                OperationId: Guid.NewGuid().ToString("N"))).ConfigureAwait(false);

        return OperationResult<TemplateDeploymentResult>.Success(result);
    }

    private static OperationResult<TemplateDeploymentResult> Failure(string title, string cause, string remediation)
        => OperationResult<TemplateDeploymentResult>.Failure(new OperationError(title, cause, remediation));

    /// <summary>
    /// Ajoute la stratégie « Mode de traitement en boucle » (Merge) aux valeurs
    /// d'une GPO de portée Ordinateur. La valeur est idempotente : elle n'est
    /// jamais dupliquée si le modèle la contient déjà.
    /// </summary>
    internal static IReadOnlyList<GpoRegistrySetting> WithLoopbackMerge(IReadOnlyList<GpoRegistrySetting> settings)
    {
        var list = settings.ToList();
        if (list.Any(setting =>
                setting.Key.Equals(LoopbackKey, StringComparison.OrdinalIgnoreCase)
                && setting.ValueName.Equals("UserPolicyMode", StringComparison.OrdinalIgnoreCase)))
        {
            return list;
        }

        list.Add(new GpoRegistrySetting
        {
            Key = LoopbackKey,
            ValueName = "UserPolicyMode",
            ValueType = "DWord",
            // 1 = Merge (les paramètres utilisateur de la GPO s'ajoutent à ceux
            // de l'utilisateur) ; 2 = Replace (remplacement complet).
            Value = "1",
        });

        return list;
    }

    private async Task<IReadOnlyList<OuInfo>> GetAllOusAsync(IPowerShellExecutor executor, CancellationToken cancellationToken)
    {
        var result = await executor.ExecuteAsync(new ListOusCommand(), cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || result.TimedOut) { return Array.Empty<OuInfo>(); }
        return new AdOutputParser().ParseOus(result.StandardOutput);
    }

    private static string BuildGpoName(string displayName)
        => "ADOps — " + Trim(displayName, 112);

    private static string Trim(string value, int max)
        => value.Length <= max ? value : value[..(max - 1)].TrimEnd() + "…";

    private static Guid ParseGuid(string json, string property)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty(property, out var element))
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

    internal static string BuildGroupName(string templateId)
    {
        var slug = new string(templateId.Where(char.IsLetterOrDigit).Take(8).ToArray());

        uint hash = 2166136261;
        foreach (var character in templateId.ToUpperInvariant())
        {
            hash ^= character;
            hash *= 16777619;
        }

        return "ADOps-" + slug + "-" + (hash % 10000).ToString("D4", CultureInfo.InvariantCulture);
    }

    /// <summary>Affiche le nom lisible d'une OU à partir de son DistinguishedName.</summary>
    private static string DisplayOu(string distinguishedName)
    {
        var first = distinguishedName.Split(',', 2)[0];
        var index = first.IndexOf('=');
        return index >= 0 && index + 1 < first.Length ? first[(index + 1)..] : distinguishedName;
    }

    /// <summary>Construit le DN du domaine à partir de son FQDN (ex: DC=sarytany,DC=local).</summary>
    private static string BuildDomainDistinguishedName(string domainFqdn)
    {
        if (string.IsNullOrWhiteSpace(domainFqdn)) return "DC=local";
        var parts = domainFqdn.Split('.');
        if (parts.Length >= 2)
        {
            var dnsParts = parts.Select(p => "DC=" + p).Reverse();
            return string.Join(",", dnsParts);
        }
        return "DC=" + domainFqdn;
    }

    /// <summary>Crée un résultat d'échec standardisé.</summary>
    private static TemplateDeploymentOutcome Failed(string templateId, string error, string gpoName = "")
        => new() { TemplateId = templateId, GpoName = gpoName, IsSuccess = false, Error = error };

    /// <summary>
    /// Extrait la liste des GPO depuis la sortie d'un <c>GetGpResultCommand</c>.
    /// Retourne null quand aucun rapport exploitable n'est présent (échec réel),
    /// une liste — éventuellement vide — quand le JSON est lisible (succès).
    /// </summary>
    private static IReadOnlyList<string>? TryExtractGpoNames(string stdout)
    {
        var json = PowerShellOutputParser.ExtractJson(stdout);
        if (json.Length == 0 || json[0] != '{' || !json.Contains("\"Computer\""))
        {
            return null;
        }

        return GpResultReportParser.FromCommandOutput(json);
    }

    /// <summary>
    /// Énumère les postes clients du domaine pour le ciblage hybride des GPO de
    /// portée Ordinateur. Les contrôleurs de domaine et les serveurs sont exclus :
    /// la stratégie est destinée aux postes de travail où se connectent les
    /// utilisateurs ciblés.
    /// </summary>
    private async Task<IReadOnlyList<ComputerInfo>> GetClientComputersAsync(
        IPowerShellExecutor executor,
        CancellationToken cancellationToken)
    {
        var result = await executor.ExecuteAsync(new ListComputersCommand(), cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || result.TimedOut)
        {
            _logger.Warning("Énumération des postes clients impossible (code " + result.ExitCode + ").");
            return Array.Empty<ComputerInfo>();
        }

        return new AdOutputParser().ParseComputers(result.StandardOutput)
            .Where(computer => computer.Enabled)
            .Where(computer => !string.IsNullOrWhiteSpace(computer.OuDistinguishedName))
            .Where(computer => !computer.OuDistinguishedName!.Contains("OU=Domain Controllers", StringComparison.OrdinalIgnoreCase))
            .Where(computer => string.IsNullOrWhiteSpace(computer.OperatingSystem)
                || !computer.OperatingSystem!.Contains("Server", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task<OperationResult<string>> DetachFromUserAsync(
        DomainContext context,
        GpoDeploymentRecord record,
        IProgress<ExecutionLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(record);

        if (string.IsNullOrWhiteSpace(record.FilterGroup))
        {
            return OperationResult<string>.Failure(new OperationError(
                "Détachement individuel impossible",
                "Cette GPO a été déployée sans filtre de sécurité : elle est liée à l'OU entière.",
                "Utilisez la page Déploiements pour retirer le lien, ou redéployez avec le filtrage AD DS activé."));
        }

        var executor = context.ExecutorMode == ExecutorMode.Remote
            ? _executors.CreateRemote(context.ServerFqdn, context.Credentials)
            : _executors.Local;

        try
        {
            progress?.Report(ExecutionLine.Command("Remove-ADGroupMember — retrait de « " + record.ComputerName + " » du ciblage"));
            var removal = await executor.ExecuteAsync(
                new RemoveAdGroupMemberCommand(record.FilterGroup, record.ComputerDistinguishedName),
                cancellationToken).ConfigureAwait(false);

            if (removal.ExitCode != 0 || removal.TimedOut)
            {
                var error = _interpreter.Interpret(removal, "Retrait du ciblage AD DS");
                return OperationResult<string>.Failure(error);
            }

            progress?.Report(ExecutionLine.Success("« " + record.ComputerName + " » retiré du groupe « " + record.FilterGroup + " »."));

            var remote = _executors.CreateRemote(record.ComputerName, context.Credentials);
            var update = await remote.ExecuteAsync(new InvokeGpUpdateCommand(record.ComputerName), cancellationToken).ConfigureAwait(false);
            if (update.TimedOut
                || (update.ExitCode != 0 && !update.StandardOutput.Contains("\"Computer\"", StringComparison.Ordinal)))
            {
                progress?.Report(ExecutionLine.Warning("Retrait effectué, mais gpupdate a échoué sur « " + record.ComputerName + " »."));
            }
            else
            {
                progress?.Report(ExecutionLine.Success("Stratégie actualisée sur « " + record.ComputerName + " »."));
            }

            await _audit.LogAsync(
                AuditActions.GpoDetach,
                record.GpoId.ToString(),
                AuditResult.Success,
                "GPO « " + record.GpoName + " » détachée de « " + record.ComputerName + " ».",
                "Groupe : " + record.FilterGroup,
                new AuditContext(
                    Server: context.ServerFqdn,
                    Domain: context.DomainFqdn,
                    ObjectGuid: record.GpoId.ToString(),
                    Targets: record.ComputerName,
                    RequestedState: "Détachement du poste",
                    OperationId: Guid.NewGuid().ToString("N"))).ConfigureAwait(false);

            return OperationResult<string>.Success(
                "« " + record.GpoName + " » n'est plus appliquée à « " + record.ComputerName + " ».");
        }
        catch (Exception ex)
        {
            _logger.Error("Détachement de la GPO " + record.GpoName + " échoué", ex);
            return OperationResult<string>.Failure(new OperationError(
                "Détachement impossible", ex.Message, "Vérifiez WinRM et l'appartenance au groupe de filtrage."));
        }
    }

    public async Task<OperationResult<IReadOnlyList<string>>> VerifyComputerAsync(
        DomainContext context,
        string computerName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(computerName))
        {
            return OperationResult<IReadOnlyList<string>>.Failure(new OperationError("Poste non précisé"));
        }

        try
        {
            var remote = _executors.CreateRemote(computerName.Trim(), context.Credentials);
            var result = await remote.ExecuteAsync(new GetGpResultCommand(computerName.Trim()), cancellationToken).ConfigureAwait(false);
            if (result.TimedOut)
            {
                return OperationResult<IReadOnlyList<string>>.Failure(
                    _interpreter.Interpret(result, "Vérification des GPO (gpresult)"));
            }

            // Un code de sortie non nul peut venir du seul transport (erreur sérialisée
            // relayée par Invoke-Command) alors que le rapport gpresult est bien arrivé.
            var names = TryExtractGpoNames(result.StandardOutput);
            if (names is null)
            {
                return OperationResult<IReadOnlyList<string>>.Failure(
                    _interpreter.Interpret(result, "Vérification des GPO (gpresult)"));
            }

            if (result.ExitCode != 0)
            {
                _logger.Warning(
                    "Vérification gpresult sur « " + computerName + " » : résultat lu malgré un code de sortie "
                    + result.ExitCode + " (bruit de transport WinRM).");
            }

            await _audit.LogAsync(
                AuditActions.GpoVerify,
                computerName.Trim(),
                AuditResult.Success,
                "Vérification des GPO de « " + computerName.Trim() + " » : " + names.Count + " GPO appliquée(s).",
                string.Join(", ", names),
                new AuditContext(
                    Server: context.ServerFqdn,
                    Domain: context.DomainFqdn,
                    Targets: computerName.Trim(),
                    OperationId: Guid.NewGuid().ToString("N"))).ConfigureAwait(false);

            return OperationResult<IReadOnlyList<string>>.Success(names);
        }
        catch (Exception ex)
        {
            return OperationResult<IReadOnlyList<string>>.Failure(new OperationError(
                "Vérification impossible", ex.Message, "Vérifiez que WinRM est activé sur le poste."));
        }
    }

    public async Task<OperationResult<string>> RefreshComputerAsync(
        DomainContext context,
        string computerName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(computerName))
        {
            return OperationResult<string>.Failure(new OperationError("Poste non précisé"));
        }

        var target = computerName.Trim();
        try
        {
            var remote = _executors.CreateRemote(target, context.Credentials);
            var update = await remote.ExecuteAsync(new InvokeGpUpdateCommand(target), cancellationToken).ConfigureAwait(false);
            if (update.TimedOut)
            {
                return OperationResult<string>.Failure(_interpreter.Interpret(update, "Actualisation (gpupdate)"));
            }

            // Comme pour gpresult : un JSON « Computer » sur stdout prouve que le
            // scriptblock distant a abouti, même si le transport a renvoyé un code 1.
            var updateReported = update.ExitCode == 0
                || update.StandardOutput.Contains("\"Computer\"", StringComparison.Ordinal);
            if (!updateReported)
            {
                return OperationResult<string>.Failure(_interpreter.Interpret(update, "Actualisation (gpupdate)"));
            }

            if (update.ExitCode != 0)
            {
                _logger.Warning(
                    "gpupdate sur « " + target + " » : succès rapporté malgré un code de sortie "
                    + update.ExitCode + " (bruit de transport WinRM).");
            }

            await _audit.LogAsync(
                AuditActions.GpoRefresh,
                target,
                AuditResult.Success,
                "Stratégie actualisée sur « " + target + " » (gpupdate /force).",
                null,
                new AuditContext(
                    Server: context.ServerFqdn,
                    Domain: context.DomainFqdn,
                    Targets: target,
                    OperationId: Guid.NewGuid().ToString("N"))).ConfigureAwait(false);

            return OperationResult<string>.Success(
                "gpupdate /force exécuté sur « " + target + " ». Les stratégies sont à jour.");
        }
        catch (Exception ex)
        {
            _logger.Warning("Actualisation de " + target + " impossible : " + ex.Message);
            return OperationResult<string>.Failure(new OperationError(
                "Actualisation impossible", ex.Message, "Vérifiez que WinRM est activé et que le poste est allumé."));
        }
    }
}