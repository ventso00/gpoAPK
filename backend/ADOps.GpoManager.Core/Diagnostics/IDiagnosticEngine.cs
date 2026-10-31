using ADOps.GpoManager.Core.Models;

namespace ADOps.GpoManager.Core.Diagnostics;

/// <summary>Descripteur d'un contrôle connu (ordre d'exécution, dépendances, famille).</summary>
public sealed class DiagnosticCheckDescriptor
{
    public required string CheckId { get; init; }
    public required string Name { get; init; }
    public DiagnosticCategory Category { get; init; }
    public int Order { get; init; }

    /// <summary>Contrôles dont dépend celui-ci (si l'un échoue, celui-ci est marqué « non exécuté »).</summary>
    public IReadOnlyList<string> DependsOn { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Moteur de diagnostic intelligent : exécute les contrôles en séquence logique,
/// isole les causes racines et produit un <see cref="DiagnosticReport"/> standardisé.
/// Aucune modification système n'est effectuée par le moteur (diagnostic seul).
/// </summary>
public interface IDiagnosticEngine
{
    /// <summary>Contrôles connus, dans l'ordre d'exécution (alimente la grille d'état de l'interface).</summary>
    IReadOnlyList<DiagnosticCheckDescriptor> Checks { get; }

    /// <summary>Exécute tous les contrôles applicables et isole les causes racines.</summary>
    Task<DiagnosticReport> RunAllAsync(DomainContext context, CancellationToken cancellationToken = default);

    /// <summary>Exécute un contrôle unique (post-vérification après remédiation).</summary>
    Task<DiagnosticCheckResult> RunCheckAsync(
        DomainContext context,
        string checkId,
        CancellationToken cancellationToken = default);
}
