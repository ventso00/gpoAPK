using System.Collections.ObjectModel;
using System.Windows;
using ADOps.GpoManager.App.Services;
using ADOps.GpoManager.Core.Diagnostics;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ADOps.GpoManager.App.ViewModels;

/// <summary>
/// Page « Diagnostic &amp; Réparation » :
/// - grille d'état des services (connectivité, DNS/SRV, WinRM, RSAT, authentification, GPO) ;
/// - barre d'actions : Diagnostiquer tout / Réparer les problèmes détectés (aperçu + confirmation) /
///   Re-tester la connexion ;
/// - mode diagnostic (DryRun) : aucune modification système ;
/// - journalisation de chaque contrôle et de chaque correction (jamais de secret).
/// </summary>
public sealed partial class DiagnosticsViewModel : ViewModelBase
{
    private readonly IDiagnosticEngine _engine;
    private readonly IRemediationService _remediation;
    private readonly ConnectionState _connection;
    private readonly ILogger _logger;
    private DiagnosticReport? _report;
    private bool _running;

    public ObservableCollection<DiagnosticCheckResult> Checks { get; } = new();

    public ObservableCollection<RemediationPlanItem> PlanItems { get; } = new();

    public ObservableCollection<RemediationExecutionResult> Results { get; } = new();

    [ObservableProperty]
    private string _targetController = "—";

    [ObservableProperty]
    private string _targetIp = "—";

    [ObservableProperty]
    private string _detectedDomain = "—";

    [ObservableProperty]
    private string _modeText = "—";

    [ObservableProperty]
    private string _lastRunText = "Aucun diagnostic lancé";

    [ObservableProperty]
    private string _summaryText = "Lancez un diagnostic pour établir l'état des services.";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _showStatus;

    [ObservableProperty]
    private bool _isStatusError;

    /// <summary>Mode diagnostic : simule les corrections sans rien modifier (activé par défaut).</summary>
    [ObservableProperty]
    private bool _isDryRun = true;

    [ObservableProperty]
    private DiagnosticCheckResult? _selectedCheck;

    [ObservableProperty]
    private bool _showPlan;

    [ObservableProperty]
    private string _planHeaderText = "Aucun plan de remédiation";

    [ObservableProperty]
    private bool _showResults;

    [ObservableProperty]
    private string _resultsHeaderText = string.Empty;

    public DiagnosticsViewModel(
        IDiagnosticEngine engine,
        IRemediationService remediation,
        ConnectionState connection,
        ILogger logger)
    {
        _engine = engine;
        _remediation = remediation;
        _connection = connection;
        _logger = logger;
    }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        RefreshHeader();

        // Premier affichage : le diagnostic est lancé automatiquement.
        if (Checks.Count == 0 && _report is null && !_running)
        {
            _ = DiagnoseAllAsync();
        }
    }

    private void RefreshHeader()
    {
        var connected = _connection.IsConnected && _connection.Current is not null;
        TargetController = connected ? _connection.Current!.ServerFqdn : "—";
        DetectedDomain = connected ? _connection.Current!.DomainFqdn : "—";
        ModeText = connected
            ? (_connection.Current!.ExecutorMode == ExecutorMode.Local ? "local (RSAT)" : "distant (WinRM)")
            : "—";
        TargetIp = _report?.TargetIp ?? "—";
    }

    private bool TryGetContext(out DomainContext context)
    {
        context = _connection.Current!;
        if (_connection.IsConnected && context is not null)
        {
            return true;
        }

        SetStatus(true, "Aucun domaine connecté : le diagnostic nécessite une connexion.");
        return false;
    }

    /// <summary>Diagnostic complet : tous les contrôles, dans l'ordre, avec isolation des causes racines.</summary>
    [RelayCommand]
    private async Task DiagnoseAllAsync()
    {
        if (_running)
        {
            return;
        }

        if (!TryGetContext(out var context))
        {
            return;
        }

        _running = true;
        IsBusy = true;
        SetStatus(false, "Diagnostic en cours…");
        _logger.Information("Diagnostic complet démarré (cible " + context.ServerFqdn + ").");

        try
        {
            _report = await _engine.RunAllAsync(context);
            ApplyReport(_report);
            LastRunText = "Dernier diagnostic : " + _report.GeneratedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

            var summary = _report.IsHealthy
                ? "Tous les contrôles sont au vert (" + _report.PassedCount + " réussis, " + _report.WarningCount + " avertissements, " + _report.SkippedCount + " non exécutés)."
                : _report.FailedCount + " contrôle(s) en échec, " + _report.WarningCount + " avertissement(s), " + _report.SkippedCount + " non exécuté(s).";
            SummaryText = _report.RootCauses.Count > 0
                ? summary + "\nCauses racines : " + string.Join(" ", _report.RootCauses)
                : summary;

            SetStatus(!_report.IsHealthy, _report.IsHealthy
                ? "Diagnostic terminé : aucun problème bloquant détecté."
                : "Diagnostic terminé : des problèmes ont été détectés. Utilisez « Réparer les problèmes détectés » pour obtenir un plan de correction.");
        }
        catch (Exception ex)
        {
            _logger.Error("Diagnostic complet échoué", ex);
            SetStatus(true, "Échec du diagnostic : " + ex.Message);
        }
        finally
        {
            IsBusy = false;
            _running = false;
        }
    }

    /// <summary>Construit le plan de remédiation prévisualisable (aucune application).</summary>
    [RelayCommand]
    private void BuildRepairPlan()
    {
        if (_report is null)
        {
            SetStatus(true, "Lancez d'abord un diagnostic complet.");
            return;
        }

        var plan = _remediation.BuildPlan(_report);
        PlanItems.Clear();
        foreach (var item in plan.Items)
        {
            PlanItems.Add(item);
        }

        ShowPlan = plan.Items.Count > 0;
        var fixable = plan.Items.Count(item => item.Action.CanAutoFix);
        PlanHeaderText = plan.IsEmpty
            ? "Aucune correction nécessaire : tous les contrôles applicables sont au vert."
            : plan.Items.Count + " correction(s) proposée(s), dont " + fixable + " applicable(s) automatiquement."
              + (plan.RequiresElevation ? " Certaines corrections nécessitent les droits administrateur." : string.Empty);

        _logger.Information("Plan de remédiation construit : " + plan.Items.Count + " action(s), " + fixable + " automatisable(s).");
        SetStatus(false, "Plan de remédiation prêt : vérifiez les corrections cochées, puis appliquez (le mode diagnostic simulé peut rester actif).");
    }

    /// <summary>
    /// Applique les corrections cochées — uniquement après confirmation explicite
    /// (le mode diagnostic reste honoré s'il est actif).
    /// </summary>
    [RelayCommand]
    private async Task ExecutePlanAsync()
    {
        if (!TryGetContext(out var context))
        {
            return;
        }

        var selected = PlanItems.Where(item => item.IsSelected).ToList();
        if (selected.Count == 0)
        {
            SetStatus(true, "Aucune correction cochée : sélectionnez au moins une action applicable.");
            return;
        }

        var autoRunnable = selected.Where(item => item.Action.CanAutoFix).ToList();
        if (autoRunnable.Count == 0)
        {
            SetStatus(true, "Les corrections cochées sont manuelles : suivez leur description (aucune modification automatique).");
            return;
        }

        var elevationWarning = selected.Any(item => item.Action.RequiresElevation)
            ? "\n\nCertaines actions nécessitent les droits administrateur (session élevée)."
            : string.Empty;
        var dryRunNotice = IsDryRun
            ? "\n\nMODE DIAGNOSTIC ACTIF : les actions seront SIMULÉES, aucune modification ne sera effectuée."
            : "\n\nLes modifications vont être appliquées sur ce poste.";

        var confirmation = MessageBox.Show(
            "Appliquer " + autoRunnable.Count + " correction(s) cochée(s) ?\n\n"
            + string.Join("\n", autoRunnable.Select(item => "• " + item.Action.Title))
            + elevationWarning + dryRunNotice,
            "Confirmation — Réparation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        IsBusy = true;
        SetStatus(false, IsDryRun ? "Simulation des corrections (mode diagnostic)…" : "Application des corrections…");

        try
        {
            var plan = new RemediationPlan { Items = PlanItems.ToList() };
            var results = await _remediation.ExecuteAsync(context, plan, confirmed: true, dryRun: IsDryRun);

            Results.Clear();
            foreach (var result in results)
            {
                Results.Add(result);
            }

            ShowResults = results.Count > 0;
            ResultsHeaderText = results.Count + " action(s) traitée(s) — "
                + results.Count(result => result.IsSuccess) + " réussie(s)";

            // Post-vérification globale : les contrôles impactés sont rejoués.
            var checkIds = results
                .Where(result => result.Action.TargetCheckId is not null)
                .Select(result => result.Action.TargetCheckId!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (checkIds.Count > 0)
            {
                _report = await _remediation.RetestAsync(context, checkIds);
                ApplyReport(_report);
                LastRunText = "Post-vérification : " + DateTimeOffset.UtcNow.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            }

            SetStatus(results.Any(result => !result.IsSuccess),
                IsDryRun
                    ? "Simulation terminée : aucune modification effectuée."
                    : "Corrections appliquées. Les contrôles impactés ont été rejoués (voir la grille d'état).");
        }
        catch (Exception ex)
        {
            _logger.Error("Application des corrections échouée", ex);
            SetStatus(true, "Échec de l'application : " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Re-teste la connexion : diagnostic complet après correction.</summary>
    [RelayCommand]
    private async Task RetestAsync()
    {
        ShowPlan = false;
        ShowResults = false;
        await DiagnoseAllAsync();
    }

    private void ApplyReport(DiagnosticReport report)
    {
        Checks.Clear();
        foreach (var result in report.Results)
        {
            Checks.Add(result);
        }

        RefreshHeader();
        TargetIp = report.TargetIp ?? "—";
    }

    private void SetStatus(bool isError, string message)
    {
        IsStatusError = isError;
        StatusMessage = message;
        ShowStatus = !string.IsNullOrWhiteSpace(message);
    }
}
