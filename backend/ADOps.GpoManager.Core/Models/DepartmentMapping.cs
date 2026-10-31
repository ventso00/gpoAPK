namespace ADOps.GpoManager.Core.Models;

/// <summary>
/// Correspondance département ↔ OU, stockée dans la base locale de l'application.
/// L'OU reste dans AD DS : l'application ne crée, ne renomme, ne déplace jamais d'OU.
/// </summary>
public sealed class DepartmentMapping
{
    public long Id { get; init; }
    public string DepartmentName { get; init; } = string.Empty;
    public string? OuDistinguishedName { get; init; }
    public string? OuName { get; init; }
    public int ComputerCount { get; init; }
    public DateTimeOffset LastSeenUtc { get; init; }
}
