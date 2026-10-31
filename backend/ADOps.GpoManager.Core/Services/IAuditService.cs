using ADOps.GpoManager.Core.Models;

namespace ADOps.GpoManager.Core.Services;

/// <summary>Service d'audit : chaque opération est enregistrée (timestamp, utilisateur, action, résultat).</summary>
public interface IAuditService
{
    Task LogAsync(string action, string target, AuditResult result, string message, string? details = null, AuditContext? context = null);
    Task<IReadOnlyList<AuditEntry>> GetRecentAsync(int maxEntries = 200);
    Task<int> GetCountAsync();
}
