using ADOps.GpoManager.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ADOps.GpoManager.App.ViewModels;

/// <summary>
/// Adaptateur d'affichage d'une étape du Health Check. Observable : une étape
/// pré-affichée « en attente » est mise à jour en place quand le diagnostic
/// la termine (animation du health check étape par étape).
/// </summary>
public sealed partial class HealthCheckStepViewModel : ObservableObject
{
    public string Name { get; }

    [ObservableProperty]
    private CheckStatus _status;

    [ObservableProperty]
    private string _statusText;

    [ObservableProperty]
    private string _message;

    [ObservableProperty]
    private string _durationText;

    [ObservableProperty]
    private string? _remediation;

    public HealthCheckStepViewModel(HealthCheckStep step)
    {
        Name = step.Name;
        _status = step.Status;
        _statusText = ToStatusText(step.Status);
        _message = step.Message;
        _durationText = ToDurationText(step.Duration);
        _remediation = step.Remediation;
    }

    /// <summary>Étape pré-affichée avant exécution (pastille grise, « En attente… »).</summary>
    public static HealthCheckStepViewModel Pending(string name)
        => new(new HealthCheckStep(name, CheckStatus.NotRun, string.Empty, TimeSpan.Zero));

    /// <summary>Applique le résultat définitif d'une étape terminée.</summary>
    public void Apply(HealthCheckStep step)
    {
        Status = step.Status;
        StatusText = ToStatusText(step.Status);
        Message = step.Message;
        DurationText = ToDurationText(step.Duration);
        Remediation = step.Remediation;
    }

    private static string ToStatusText(CheckStatus status) => status switch
    {
        CheckStatus.Passed => "OK",
        CheckStatus.Warning => "Attention",
        CheckStatus.Failed => "Échec",
        CheckStatus.Skipped => "Ignoré",
        CheckStatus.NotRun => "En attente…",
        _ => "—",
    };

    private static string ToDurationText(TimeSpan duration) => duration.TotalMilliseconds < 1000
        ? duration.TotalMilliseconds.ToString("0") + " ms"
        : duration.TotalSeconds.ToString("0.0") + " s";
}
