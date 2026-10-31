namespace ADOps.GpoManager.Core.Models;

public enum CheckStatus
{
    NotRun = 0,
    Passed = 1,
    Warning = 2,
    Failed = 3,
    Skipped = 4,
}

/// <summary>Résultat d'une étape du Health Check.</summary>
public sealed class HealthCheckStep
{
    public string Name { get; }
    public CheckStatus Status { get; }
    public string Message { get; }
    public TimeSpan Duration { get; }
    public string? Remediation { get; }

    public HealthCheckStep(string name, CheckStatus status, string message, TimeSpan duration, string? remediation = null)
    {
        Name = name;
        Status = status;
        Message = message;
        Duration = duration;
        Remediation = remediation;
    }

    public static HealthCheckStep Skipped(string name, string reason) =>
        new(name, CheckStatus.Skipped, reason, TimeSpan.Zero);
}

/// <summary>Rapport complet du Health Check d'un serveur cible.</summary>
public sealed class HealthCheckReport
{
    public string TargetFqdn { get; }
    public string DomainFqdn { get; }
    public IReadOnlyList<HealthCheckStep> Steps { get; }
    public bool IsHealthy { get; }
    public ExecutorMode RecommendedExecutorMode { get; }
    public string Summary { get; }

    public HealthCheckReport(
        string targetFqdn,
        string domainFqdn,
        IReadOnlyList<HealthCheckStep> steps,
        bool isHealthy,
        ExecutorMode recommendedExecutorMode,
        string summary)
    {
        TargetFqdn = targetFqdn;
        DomainFqdn = domainFqdn;
        Steps = steps;
        IsHealthy = isHealthy;
        RecommendedExecutorMode = recommendedExecutorMode;
        Summary = summary;
    }
}
