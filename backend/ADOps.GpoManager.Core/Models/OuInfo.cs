namespace ADOps.GpoManager.Core.Models;

/// <summary>Unité d'organisation lue dans AD DS (source de vérité : l'annuaire).</summary>
public sealed class OuInfo
{
    public string Name { get; init; } = string.Empty;
    public string DistinguishedName { get; init; } = string.Empty;
    public string? CanonicalName { get; init; }
    public string? ParentDistinguishedName { get; init; }
}
