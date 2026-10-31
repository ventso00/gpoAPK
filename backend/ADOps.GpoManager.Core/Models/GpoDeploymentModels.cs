namespace ADOps.GpoManager.Core.Models;

// Modèles du moteur de déploiement des GPO :
// planification (validation + rapport d'impact), application et résultats.

/// <summary>
/// Impact d'une GPO sur une cible (OU) : les utilisateurs et ordinateurs
/// réellement situés dans le sous-arbre de l'OU, lus dans AD DS.
/// </summary>
public sealed class GpoImpactEntry
{
    public Guid GpoId { get; init; }
    public string GpoName { get; init; } = string.Empty;
    public string TargetOuDistinguishedName { get; init; } = string.Empty;
    public string TargetOuName { get; init; } = string.Empty;
    public IReadOnlyList<UserInfo> AffectedUsers { get; init; } = Array.Empty<UserInfo>();
    public IReadOnlyList<ComputerInfo> AffectedComputers { get; init; } = Array.Empty<ComputerInfo>();

    /// <summary>Vrai si un lien identique existe déjà dans AD DS (New-GPLink est idempotent).</summary>
    public bool AlreadyLinked { get; init; }

    public int AffectedUserCount => AffectedUsers.Count;
    public int AffectedComputerCount => AffectedComputers.Count;
}

/// <summary>
/// Plan de déploiement validé : ce qui sera appliqué, à qui, avec les
/// avertissements (doublons de liens) et les erreurs de validation.
/// Le plan est le résultat de l'étape de validation préalable au déploiement.
/// </summary>
public sealed class GpoDeploymentPlan
{
    public IReadOnlyList<GpoImpactEntry> Entries { get; init; } = Array.Empty<GpoImpactEntry>();

    /// <summary>Utilisateurs uniques impactés (une fois, même s'il figure dans plusieurs entrées).</summary>
    public int TotalAffectedUsers { get; init; }

    /// <summary>Ordinateurs uniques impactés.</summary>
    public int TotalAffectedComputers { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public bool IsValid => Errors.Count == 0 && Entries.Count > 0;
}

/// <summary>Résultat d'une opération de lien (New-GPLink ou Remove-GPLink).</summary>
public sealed class GpoLinkOperationResult
{
    public Guid GpoId { get; init; }
    public string GpoName { get; init; } = string.Empty;
    public string TargetDistinguishedName { get; init; } = string.Empty;
    public string? TargetName { get; init; }
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? Error { get; init; }
}

/// <summary>Résultat global d'un déploiement : une opération par lien appliqué.</summary>
public sealed class GpoDeploymentResult
{
    public IReadOnlyList<GpoLinkOperationResult> Operations { get; init; } = Array.Empty<GpoLinkOperationResult>();

    public int Succeeded { get; init; }
    public int Failed { get; init; }

    public bool IsSuccess => Succeeded > 0 && Failed == 0;
    public bool IsPartial => Succeeded > 0 && Failed > 0;

    public string SummaryText => IsSuccess
        ? Succeeded + " lien(s) appliqué(s)."
        : IsPartial
            ? Succeeded + " réussi(s), " + Failed + " échec(s)."
            : Failed + " échec(s).";
}
