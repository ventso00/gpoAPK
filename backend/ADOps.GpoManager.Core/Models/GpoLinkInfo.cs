namespace ADOps.GpoManager.Core.Models;

public enum GpoLinkTargetType
{
    Domain = 0,
    OrganizationalUnit = 1,
}

/// <summary>Lien de GPO (GPO liée à un domaine ou à une OU), tel que retourné par Get-GPInheritance.</summary>
public sealed class GpoLinkInfo
{
    public Guid GpoId { get; init; }
    public string TargetDistinguishedName { get; init; } = string.Empty;
    public GpoLinkTargetType TargetType { get; init; }
    public int Order { get; init; }
    public bool IsEnabled { get; init; }

    /// <summary>Nom d'affichage de la GPO (joint depuis le cache local ; null si inconnu).</summary>
    public string? GpoName { get; init; }

    /// <summary>Nom si connu, sinon le GUID : les listes restent lisibles sans reconnaissance de GUID.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(GpoName) ? GpoId.ToString() : GpoName!;
}
