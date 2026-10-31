namespace ADOps.GpoManager.Core.Models;

public enum AuditResult
{
    Information = 0,
    Success = 1,
    Warning = 2,
    Failure = 3,
}

/// <summary>
/// Entrée d'audit (cahier des charges §12) : auteur, serveur, domaine, action,
/// objet/GUID, cibles, état initial/connu, état demandé, résultat, horodatage UTC,
/// durée, code d'erreur et identifiant d'opération.
/// </summary>
public sealed class AuditEntry
{
    public long Id { get; init; }
    public DateTimeOffset TimestampUtc { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public AuditResult Result { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? Details { get; init; }
    public string? OperationId { get; init; }
    public string? Server { get; init; }
    public string? Domain { get; init; }
    public string? ObjectGuid { get; init; }
    public string? Targets { get; init; }
    public string? InitialState { get; init; }
    public string? RequestedState { get; init; }
    public string? ErrorCode { get; init; }
    public TimeSpan? Duration { get; init; }
}

/// <summary>Noms d'actions auditables (constants, jamais saisies par l'utilisateur).</summary>
public static class AuditActions
{
    public const string ApplicationStarted = "ApplicationStarted";
    public const string Login = "Login";
    public const string HealthCheck = "HealthCheck";
    public const string InventorySync = "InventorySync";
    public const string AvailabilityCheck = "AvailabilityCheck";
    public const string DepartmentMapping = "DepartmentMapping";
    public const string GpoList = "GpoList";
    public const string GpoReport = "GpoReport";
    public const string GpoCreate = "GpoCreate";
    public const string RsopQuery = "RsopQuery";
    public const string SettingsChanged = "SettingsChanged";
    public const string DirectoryRead = "DirectoryRead";
    public const string DirectoryWrite = "DirectoryWrite";
    public const string NetworkDiscovery = "NetworkDiscovery";
    public const string DeploymentPlan = "DeploymentPlan";
    public const string GpoLink = "GpoLink";
    public const string GpoUnlink = "GpoUnlink";
    public const string DeploymentExecute = "DeploymentExecute";
    public const string GpoTemplateApply = "GpoTemplateApply";
    public const string GpoTargeting = "GpoTargeting";
    public const string GpoDetach = "GpoDetach";
    public const string GpoVerify = "GpoVerify";
    public const string GpoRefresh = "GpoRefresh";
    public const string ComputerControl = "ComputerControl";

    // Diagnostic & réparation (moteur de diagnostic intelligent)
    public const string DiagnosticRun = "DiagnosticRun";
    public const string RemediationPlan = "RemediationPlan";
    public const string RemediationApply = "RemediationApply";
}
