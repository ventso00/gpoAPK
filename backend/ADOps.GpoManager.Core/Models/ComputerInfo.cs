namespace ADOps.GpoManager.Core.Models;

/// <summary>Objet ordinateur lu dans AD DS. AD DS reste la source de vérité.</summary>
public sealed class ComputerInfo
{
    public string Name { get; init; } = string.Empty;
    public string DistinguishedName { get; init; } = string.Empty;
    public string? SamAccountName { get; init; }
    public string? OuDistinguishedName { get; init; }
    public string? OperatingSystem { get; init; }
    public string? OperatingSystemVersion { get; init; }
    public bool Enabled { get; init; }
    public DateTimeOffset? LastLogonUtc { get; init; }

    /// <summary>Adresse IPv4 déclarée dans AD DS (attribut IPv4Address) — optionnel :
    /// il suppose que l'agent write-IP est activé sur les postes.</summary>
    public string? IpAddress { get; init; }
}
