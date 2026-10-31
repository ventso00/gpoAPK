namespace ADOps.GpoManager.Core.Models;

/// <summary>Résultat GPO effectif d'un ordinateur (issu d'un rapport réel, avec sa date).</summary>
public sealed class ComputerGpoResult
{
    public string ComputerName { get; init; } = string.Empty;
    public DateTimeOffset ReportDateUtc { get; init; }
    public string ReportSource { get; init; } = string.Empty;
    public IReadOnlyList<string> AppliedGpoNames { get; init; } = Array.Empty<string>();
    public string? RawReport { get; init; }
}
