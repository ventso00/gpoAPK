namespace ADOps.GpoManager.Core.Models;

public enum AvailabilityState
{
    Unknown = 0,
    Online = 1,
    OnlineWithoutWinRm = 2,
    DnsUnresolved = 3,
}

public enum AvailabilityCheckMethod
{
    None = 0,
    Dns = 1,
    DnsAndWinRm = 2,
}

/// <summary>
/// Disponibilité d'un ordinateur, avec la méthode utilisée.
/// Un ping échoué ne prouve pas qu'une machine est éteinte : ICMP est souvent bloqué.
/// </summary>
public sealed class ComputerHealthInfo
{
    public string ComputerName { get; init; } = string.Empty;
    public string ComputerDistinguishedName { get; init; } = string.Empty;
    public AvailabilityState State { get; init; }
    public AvailabilityCheckMethod Method { get; init; }
    public bool DnsResolved { get; init; }
    public string? DnsIpAddress { get; init; }
    public bool WinRmAvailable { get; init; }
    public DateTimeOffset CheckedAtUtc { get; init; }
    public string? Error { get; init; }
}
