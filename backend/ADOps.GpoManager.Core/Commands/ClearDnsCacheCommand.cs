namespace ADOps.GpoManager.Core.Commands;

/// <summary>
/// Remédiation DNS sans risque : vidage du cache DNS client et
/// réenregistrement dynamique (ipconfig /registerdns).
/// Aucune modification de la configuration réseau (adresses des serveurs DNS,
/// suffixes) : ces changements restent manuels par principe de sécurité.
/// </summary>
public sealed class ClearDnsCacheCommand : PowerShellCommand
{
    public override string Name => "ClearDnsCache";
    public override TimeSpan Timeout { get; } = TimeSpan.FromMinutes(1);

    public override string ScriptBody =>
        """
        $cleared = $false
        try { Clear-DnsClientCache -ErrorAction Stop; $cleared = $true } catch { $cleared = $false }
        $registered = $false
        try { & ipconfig /registerdns | Out-Null; $registered = $true } catch { $registered = $false }
        @{ Applied = $cleared; CacheCleared = $cleared; DnsReRegistered = $registered }
        """;
}
