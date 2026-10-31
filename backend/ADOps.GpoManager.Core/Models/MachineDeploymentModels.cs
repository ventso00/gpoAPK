namespace ADOps.GpoManager.Core.Models;

/// <summary>
/// Plan de déploiement exprimé en « machines et comptes » : c'est le langage
/// naturel de l'administrateur (« j'applique cette GPO à tous les postes de
/// la compta »), que le moteur traduit en liens de GPO.
/// </summary>
public sealed class MachineDeploymentPlan
{
    /// <summary>Entrées du plan : un couple (GPO × conteneur de destination).</summary>
    public IReadOnlyList<MachineDeploymentEntry> Entries { get; init; } = Array.Empty<MachineDeploymentEntry>();

    /// <summary>Comptes clients retenus comme cibles.</summary>
    public IReadOnlyList<UserInfo> TargetAccounts { get; init; } = Array.Empty<UserInfo>();

    /// <summary>Postes retenus comme cibles.</summary>
    public IReadOnlyList<ComputerInfo> TargetComputers { get; init; } = Array.Empty<ComputerInfo>();

    /// <summary>Groupe de sécurité retenu pour le filtrage (null : aucun filtrage).</summary>
    public string? SecurityGroupDn { get; init; }

    /// <summary>Nombre de liens de GPO à créer.</summary>
    public int PlannedLinkCount => Entries.Count;

    /// <summary>Nombre de comptes clients impactés.</summary>
    public int AffectedAccountCount => TargetAccounts.Count;

    /// <summary>Nombre de postes impactés.</summary>
    public int AffectedComputerCount => TargetComputers.Count;

    /// <summary>Vrai si le plan est applicable.</summary>
    public bool IsValid => Entries.Count > 0;

    /// <summary>Avertissements (déduplication, cibles hors périmètre…).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>Erreurs bloquantes.</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}

/// <summary>Une entrée du plan : une GPO et le conteneur où elle sera liée.</summary>
public sealed class MachineDeploymentEntry
{
    public Guid GpoId { get; init; }

    public string GpoName { get; init; } = string.Empty;

    /// <summary>DN de l'OU de destination (point d'ancrage du lien).</summary>
    public string TargetDistinguishedName { get; init; } = string.Empty;

    /// <summary>Libellé lisible de la cible.</summary>
    public string TargetName { get; init; } = string.Empty;

    /// <summary>Postes couverts par ce lien.</summary>
    public IReadOnlyList<string> ComputerNames { get; init; } = Array.Empty<string>();

    /// <summary>Comptes clients couverts par ce lien.</summary>
    public IReadOnlyList<string> AccountNames { get; init; } = Array.Empty<string>();

    /// <summary>Vrai si la GPO est déjà liée à cette cible.</summary>
    public bool AlreadyLinked { get; init; }
}

/// <summary>Résultat de l'application d'un plan sur des machines.</summary>
public sealed class MachineDeploymentResult
{
    /// <summary>Résultat par lien appliqué.</summary>
    public IReadOnlyList<GpoLinkOperationResult> Operations { get; init; } = Array.Empty<GpoLinkOperationResult>();

    /// <summary>Postes sur lesquels un rafraîchissement a été demandé.</summary>
    public IReadOnlyList<string> RefreshedComputers { get; init; } = Array.Empty<string>();

    /// <summary>Postes sur lesquels le rafraîchissement a échoué.</summary>
    public IReadOnlyList<string> FailedRefreshes { get; init; } = Array.Empty<string>();

    /// <summary>Comptes réellement couverts par la GPO (pour la vue « comptes concernés »).</summary>
    public IReadOnlyList<UserInfo> AppliedAccounts { get; init; } = Array.Empty<UserInfo>();

    public int Succeeded => Operations.Count(operation => operation.IsSuccess);

    public int Failed => Operations.Count(operation => !operation.IsSuccess);

    public bool IsSuccess => Failed == 0 && Operations.Count > 0;

    public string SummaryText =>
        Succeeded + " lien(s) appliqué(s), " + Failed + " échec(s)"
        + (RefreshedComputers.Count > 0 ? ", " + RefreshedComputers.Count + " poste(s) actualisé(s)" : string.Empty);
}
