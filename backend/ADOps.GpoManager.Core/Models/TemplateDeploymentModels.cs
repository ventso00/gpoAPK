namespace ADOps.GpoManager.Core.Models;

/// <summary>Sélection d'un modèle et de ses paramètres pour un déploiement réel.</summary>
public sealed class TemplateDeploymentSelection
{
    public string TemplateId { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, object?> Parameters { get; init; }
        = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Nom de GPO imposé (facultatif). Par défaut : « ADOps — &lt;modèle&gt; ».</summary>
    public string? GpoName { get; init; }
}

/// <summary>Résultat de la matérialisation et du ciblage d'un modèle.</summary>
public sealed class TemplateDeploymentOutcome
{
    public string TemplateId { get; init; } = string.Empty;
    public string GpoName { get; init; } = string.Empty;
    public Guid GpoId { get; init; }
    public int SettingsCount { get; init; }
    public IReadOnlyList<string> LinkTargets { get; init; } = Array.Empty<string>();
    public string? FilterGroup { get; init; }
    public bool IsSuccess { get; init; }
    public string? Error { get; init; }
}

/// <summary>Synthèse d'un déploiement de modèles sur des postes.</summary>
public sealed class TemplateDeploymentResult
{
    public IReadOnlyList<TemplateDeploymentOutcome> Outcomes { get; init; } = Array.Empty<TemplateDeploymentOutcome>();

    /// <summary>Traces à persister localement pour la console des postes.</summary>
    public IReadOnlyList<GpoDeploymentRecord> Records { get; init; } = Array.Empty<GpoDeploymentRecord>();

    public IReadOnlyList<string> RefreshedComputers { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FailedComputers { get; init; } = Array.Empty<string>();
    public DateTimeOffset StartedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CompletedUtc { get; init; } = DateTimeOffset.UtcNow;

    public int TotalSettings => Outcomes.Sum(outcome => outcome.SettingsCount);
    public int GpoCount => Outcomes.Count(outcome => outcome.IsSuccess);

    public string SummaryText
        => GpoCount + " GPO matérialisée(s), " + TotalSettings + " valeur(s) de registre, "
           + Outcomes.Where(outcome => outcome.IsSuccess).Sum(outcome => outcome.LinkTargets.Count)
           + " lien(s) de GPO"
           + (RefreshedComputers.Count > 0 ? ", " + RefreshedComputers.Count + " poste(s) actualisé(s)" : string.Empty)
           + (FailedComputers.Count > 0 ? ", " + FailedComputers.Count + " en échec" : string.Empty) + ".";
}

/// <summary>
/// Trace locale d'une application : permet à la console d'un poste d'afficher
/// les GPO réellement appliquées par ADOps, leurs paramètres et le groupe de
/// filtrage utilisé pour le détachement individuel.
/// </summary>
public sealed class GpoDeploymentRecord
{
    public long Id { get; init; }
    public Guid GpoId { get; init; }
    public string GpoName { get; init; } = string.Empty;
    public string TemplateId { get; init; } = string.Empty;
    public string ComputerName { get; init; } = string.Empty;
    public string ComputerDistinguishedName { get; init; } = string.Empty;
    public string OuDistinguishedName { get; init; } = string.Empty;
    public string? FilterGroup { get; init; }
    public string SettingsJson { get; init; } = "[]";
    public DateTimeOffset DeployedUtc { get; init; } = DateTimeOffset.UtcNow;
}
