namespace ADOps.GpoManager.Core.Models;

/// <summary>Gravité d'une ligne de journal d'exécution.</summary>
public enum ExecutionLevel
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Error = 3,
    Command = 4,
}

/// <summary>
/// Une ligne du terminal d'exécution : ce que l'administrateur voit
/// défiler pendant l'application d'une GPO.
/// </summary>
public sealed class ExecutionLine
{
    public ExecutionLevel Level { get; init; } = ExecutionLevel.Info;

    public string Message { get; init; } = string.Empty;

    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Horodatage local, format court (HH:mm:ss).</summary>
    public string TimeText => TimestampUtc.ToLocalTime().ToString("HH:mm:ss");

    /// <summary>Préfixe conventionnel affiché dans le terminal.</summary>
    public string Prefix => Level switch
    {
        ExecutionLevel.Success => "[OK]   ",
        ExecutionLevel.Warning => "[AVERT]",
        ExecutionLevel.Error => "[ERREUR]",
        ExecutionLevel.Command => "> ",
        _ => "[INFO]  ",
    };

    /// <summary>Texte complet, préfixe compris.</summary>
    public string DisplayText => Prefix + Message;

    public static ExecutionLine Info(string message) => new() { Level = ExecutionLevel.Info, Message = message };

    public static ExecutionLine Success(string message) => new() { Level = ExecutionLevel.Success, Message = message };

    public static ExecutionLine Warning(string message) => new() { Level = ExecutionLevel.Warning, Message = message };

    public static ExecutionLine Error(string message) => new() { Level = ExecutionLevel.Error, Message = message };

    public static ExecutionLine Command(string message) => new() { Level = ExecutionLevel.Command, Message = message };
}
