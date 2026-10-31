using ADOps.GpoManager.Core.Models;

namespace ADOps.GpoManager.Core.Diagnostics;

/// <summary>Statut standardisé d'un contrôle de diagnostic.</summary>
public enum DiagnosticStatus
{
    /// <summary>Contrôle réussi.</summary>
    Passed = 0,

    /// <summary>Contrôle réussi avec réserve (fonctionnel mais dégradé).</summary>
    Warning = 1,

    /// <summary>Contrôle en échec : une action corrective est proposée.</summary>
    Failed = 2,

    /// <summary>Contrôle non exécuté (dépendance en échec, ou annulé).</summary>
    Skipped = 3,

    /// <summary>Contrôle en cours d'exécution (affichage transitoire).</summary>
    Running = 4,
}

/// <summary>Famille de contrôle — utilisée pour regrouper l'affichage et ordonner l'exécution.</summary>
public enum DiagnosticCategory
{
    Network = 0,
    Dns = 1,
    WinRm = 2,
    Rsat = 3,
    Authentication = 4,
    GpoAccess = 5,
    System = 6,
}

/// <summary>Niveau de risque d'une action de remédiation (affiché avant confirmation).</summary>
public enum RemediationRisk
{
    /// <summary>Sans effet destructeur (ex. : vidage du cache DNS).</summary>
    Safe = 0,

    /// <summary>Modifie la configuration système (WinRM, pare-feu, RSAT) — confirmation explicite obligatoire.</summary>
    Elevated = 1,

    /// <summary>Ne peut pas être automatisé en sécurité (ex. : correction DNSSEC) — instructions manuelles.</summary>
    ManualOnly = 2,
}

/// <summary>
/// Résultat standardisé d'un contrôle de diagnostic.
/// Aucun secret n'y figure : uniquement des faits techniques et des codes d'erreur.
/// </summary>
public sealed class DiagnosticCheckResult
{
    /// <summary>Identifiant stable du contrôle (ex. : « WinRm.Ports »).</summary>
    public required string CheckId { get; init; }

    /// <summary>Libellé affiché (ex. : « Ports WinRM 5985 / 5986 »).</summary>
    public required string Name { get; init; }

    public DiagnosticCategory Category { get; init; }

    public DiagnosticStatus Status { get; set; }

    /// <summary>Code d'erreur normalisé (ex. : « WINRM_PORT_CLOSED », « SRV_RECORD_MISSING »).</summary>
    public string? ErrorCode { get; set; }

    /// <summary>Détail factuel lisible (jamais de mot de passe ni de credential).</summary>
    public string Details { get; set; } = string.Empty;

    /// <summary>Correctifs suggérés, formulés en langage technicien.</summary>
    public IReadOnlyList<string> SuggestedFixes { get; set; } = Array.Empty<string>();

    /// <summary>Vrai si la correction exige des droits administrateur locaux.</summary>
    public bool RequiresElevation { get; set; }

    /// <summary>Vrai si l'application peut appliquer le correctif automatiquement (après confirmation).</summary>
    public bool CanAutoFix { get; set; }

    public TimeSpan Duration { get; set; }

    /// <summary>Sortie JSON brute du contrôle (diagnostic uniquement, aucun secret).</summary>
    public string? RawOutput { get; set; }

    public bool IsOk => Status is DiagnosticStatus.Passed or DiagnosticStatus.Warning;
}

/// <summary>Action de remédiation proposée par le moteur, liée au contrôle qu'elle répare.</summary>
public sealed class RemediationAction
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Description { get; set; } = string.Empty;
    public RemediationRisk Risk { get; set; }
    public bool RequiresElevation { get; set; }

    /// <summary>Vrai si l'application sait exécuter l'action (commande typée dédiée).</summary>
    public bool CanAutoFix { get; set; }

    /// <summary>Nom de la commande typée exécutée par la remédiation (jamais une saisie libre).</summary>
    public string CommandName { get; set; } = string.Empty;

    /// <summary>Identifiant du contrôle à rejouer après application (post-vérification).</summary>
    public string TargetCheckId { get; set; } = string.Empty;
}

/// <summary>Élément du plan de remédiation : une action + sa case de sélection.</summary>
public sealed class RemediationPlanItem
{
    public required RemediationAction Action { get; init; }
    public string CheckName { get; set; } = string.Empty;
    public bool IsSelected { get; set; } = true;
}

/// <summary>Plan de remédiation prévisualisable : rien n'est appliqué à ce stade.</summary>
public sealed class RemediationPlan
{
    public IReadOnlyList<RemediationPlanItem> Items { get; init; } = Array.Empty<RemediationPlanItem>();
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;

    public bool IsEmpty => Items.Count == 0;
    public bool RequiresElevation => Items.Any(item => item.Action.RequiresElevation);
}

/// <summary>Résultat de l'application d'une action de remédiation, avec post-vérification.</summary>
public sealed class RemediationExecutionResult
{
    public required RemediationAction Action { get; init; }
    public bool IsSuccess { get; set; }
    public bool WasDryRun { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Error { get; set; }

    /// <summary>Statut du contrôle cible après application (null si non rejoué).</summary>
    public DiagnosticStatus? PostCheckStatus { get; set; }
}

/// <summary>Rapport de diagnostic complet du moteur.</summary>
public sealed class DiagnosticReport
{
    /// <summary>Contrôleur cible (FQDN ou adresse IP) tel que configuré.</summary>
    public required string TargetController { get; init; }

    /// <summary>Adresse IP résolue du contrôleur (si la résolution a abouti).</summary>
    public string? TargetIp { get; set; }

    public required string DetectedDomain { get; init; }

    public ExecutorMode Mode { get; init; }

    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;

    public IReadOnlyList<DiagnosticCheckResult> Results { get; init; } = Array.Empty<DiagnosticCheckResult>();

    public int PassedCount { get; init; }
    public int WarningCount { get; init; }
    public int FailedCount { get; init; }
    public int SkippedCount { get; init; }

    public bool IsHealthy => FailedCount == 0;

    /// <summary>
    /// Causes racines isolées : les échecs qui expliquent les autres
    /// (ex. : WinRM inaccessible → authentification et accès GPO non testés).
    /// </summary>
    public IReadOnlyList<string> RootCauses { get; init; } = Array.Empty<string>();

    public DiagnosticCheckResult? Find(string checkId)
        => Results.FirstOrDefault(result => string.Equals(result.CheckId, checkId, StringComparison.OrdinalIgnoreCase));
}
