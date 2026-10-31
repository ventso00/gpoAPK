namespace ADOps.GpoManager.Core.Models;

public enum SyncState
{
    NotRun = 0,
    Running = 1,
    Succeeded = 2,
    Partial = 3,
    Failed = 4,
}

/// <summary>Résumé d'un run de synchronisation (historique visible dans l'interface).</summary>
public sealed class SyncRunSummary
{
    public long Id { get; init; }
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset? FinishedUtc { get; init; }
    public SyncState State { get; init; }
    public int OrganizationalUnitsRead { get; init; }
    public int ComputersRead { get; init; }
    public int ComputersAdded { get; init; }
    public int ComputersUpdated { get; init; }
    public int ComputersRemoved { get; init; }
    public int UsersRead { get; init; }
    public int GposRead { get; init; }
    public int LinksRead { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public string? Error { get; init; }
}
