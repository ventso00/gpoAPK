using ADOps.GpoManager.Core.Security;

namespace ADOps.GpoManager.PowerShell.Host;

/// <summary>Résultat de l'exécution d'un script PowerShell.</summary>
public sealed record PowerShellExecutionResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    TimeSpan Duration)
{
    /// <summary>
    /// stderr brut tel que renvoyé par le processus quand il a été décodé
    /// (enveloppe CLIXML) ; null lorsque <see cref="StandardError"/> est déjà le texte d'origine.
    /// </summary>
    public string? RawStandardError { get; init; }

    /// <summary>
    /// Canal WinRM réellement employé, renseigné uniquement par
    /// <see cref="RemoteExecutor"/> (null pour une exécution locale).
    /// </summary>
    public WinRmTransportMode? TransportUsed { get; init; }

    /// <summary>
    /// Motif du repli HTTPS → HTTP, renseigné uniquement lorsque
    /// <see cref="TransportUsed"/> vaut <see cref="WinRmTransportMode.Http"/>
    /// alors que le transport configuré était <see cref="WinRmTransportMode.Auto"/>.
    /// </summary>
    public string? TransportFallbackReason { get; init; }

    /// <summary>Vrai si un repli en clair a eu lieu : l'appelant doit le signaler.</summary>
    public bool TransportFallbackOccurred => TransportUsed == WinRmTransportMode.Http
        && !string.IsNullOrWhiteSpace(TransportFallbackReason);
}