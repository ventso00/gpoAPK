namespace ADOps.GpoManager.Core.Models;

/// <summary>
/// Cible de déploiement gérée localement (compte client ou poste), retenue
/// par l'administrateur pour l'application d'une GPO.
/// </summary>
public sealed class AssistantTarget
{
    /// <summary>Nature de la cible (« account » ou « computer »).</summary>
    public string Kind { get; init; } = "account";

    /// <summary>DistinguishedName Active Directory de l'objet.</summary>
    public string DistinguishedName { get; init; } = string.Empty;

    /// <summary>Nom affiché.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Nom de connexion (comptes) ou nom court (postes).</summary>
    public string? SamAccountName { get; init; }

    /// <summary>Unité d'organisation de rattachement.</summary>
    public string? OuDistinguishedName { get; init; }

    /// <summary>La cible est-elle retenue pour le déploiement ?</summary>
    public bool IsSelected { get; init; } = true;

    /// <summary>Note libre de l'administrateur.</summary>
    public string? Note { get; init; }

    /// <summary>Dernière observation de la cible.</summary>
    public DateTimeOffset LastSeenUtc { get; init; } = DateTimeOffset.UtcNow;
}
