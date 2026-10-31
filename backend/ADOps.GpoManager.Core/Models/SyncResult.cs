namespace ADOps.GpoManager.Core.Models;

/// <summary>Résultat d'une synchronisation AD DS : tout ce qui a été lu depuis l'annuaire.</summary>
public sealed class SyncResult
{
    public IReadOnlyList<OuInfo> OrganizationalUnits { get; init; } = Array.Empty<OuInfo>();
    public IReadOnlyList<ComputerInfo> Computers { get; init; } = Array.Empty<ComputerInfo>();
    public IReadOnlyList<UserInfo> Users { get; init; } = Array.Empty<UserInfo>();
    public IReadOnlyList<GpoSummary> Gpos { get; init; } = Array.Empty<GpoSummary>();
    public IReadOnlyList<GpoLinkInfo> Links { get; init; } = Array.Empty<GpoLinkInfo>();
    public TimeSpan Duration { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public bool IsPartial => Warnings.Count > 0;
}
