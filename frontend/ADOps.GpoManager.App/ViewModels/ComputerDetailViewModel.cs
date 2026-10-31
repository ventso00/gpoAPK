using System.Collections.ObjectModel;
using System.Xml;
using ADOps.GpoManager.App.Services;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Services;
using ADOps.GpoManager.Infrastructure.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ADOps.GpoManager.App.ViewModels;

/// <summary>
/// Fiche d'un ordinateur : identité AD DS, GPO prévues (liens de son OU et du domaine),
/// résultat effectif (RSoP lu via gpresult sur le poste), historique et diagnostic.
/// Le résultat effectif n'est jamais deviné : il vient d'un rapport réel, daté.
/// </summary>
public sealed partial class ComputerDetailViewModel : ViewModelBase
{
    private readonly InventoryQueryRepository _inventory;
    private readonly IGpoResultService _gpoResult;
    private readonly IAuditService _audit;
    private readonly ILogger _logger;
    private readonly ConnectionState _connection;

    [ObservableProperty]
    private ComputerRow? _computer;

    [ObservableProperty]
    private string _rsopStatusText = "Non vérifié";

    [ObservableProperty]
    private string _rsopDateText = "—";

    [ObservableProperty]
    private string _rsopSourceText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _showStatus;

    [ObservableProperty]
    private bool _isStatusError;

    public ObservableCollection<GpoLinkInfo> ExpectedGpos { get; } = new();

    public ObservableCollection<string> AppliedGpos { get; } = new();

    public ObservableCollection<AuditEntryViewModel> History { get; } = new();

    public ComputerDetailViewModel(
        InventoryQueryRepository inventory,
        IGpoResultService gpoResult,
        IAuditService audit,
        ILogger logger,
        ConnectionState connection)
    {
        _inventory = inventory;
        _gpoResult = gpoResult;
        _audit = audit;
        _logger = logger;
        _connection = connection;
    }

    /// <summary>Charge la fiche d'un ordinateur (appelé par la navigation depuis le parc).</summary>
    public async void LoadComputer(ComputerRow row)
    {
        Computer = row;
        await LoadExpectedGposAsync(row.DistinguishedName);
        await LoadStoredRsopAsync(row.DistinguishedName);
        await LoadHistoryAsync(row.Name);
    }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
    }

    private async Task LoadExpectedGposAsync(string distinguishedName)
    {
        try
        {
            var links = await _inventory.GetLinksForComputerAsync(distinguishedName);
            ExpectedGpos.Clear();
            foreach (var link in links.OrderBy(l => l.Order))
            {
                ExpectedGpos.Add(link);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Lecture des GPO prévues échouée", ex);
        }
    }

    private async Task LoadStoredRsopAsync(string distinguishedName)
    {
        try
        {
            var report = await _inventory.GetRsopReportAsync(distinguishedName);
            if (report is null)
            {
                RsopStatusText = "Non vérifié";
                RsopDateText = "—";
                RsopSourceText = string.Empty;
                AppliedGpos.Clear();
                return;
            }

            RsopDateText = report.ReportDateUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            RsopSourceText = report.ReportSource;
            RsopStatusText = report.AppliedGpoNames.Count == 0
                ? "Rapport obtenu — aucune GPO appliquée"
                : "Rapport obtenu — " + report.AppliedGpoNames.Count + " GPO appliquée(s)";

            AppliedGpos.Clear();
            foreach (var name in report.AppliedGpoNames)
            {
                AppliedGpos.Add(name);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Lecture du rapport RSoP stocké échouée", ex);
        }
    }

    /// <summary>Obtention du rapport RSoP en direct : gpresult s'exécute sur le poste via WinRM.</summary>
    [RelayCommand]
    private async Task RefreshRsopAsync()
    {
        if (_computer is null)
        {
            return;
        }

        IsBusy = true;
        SetStatus(false, string.Empty);

        try
        {
            var context = _connection.Current;
            if (context is null)
            {
                SetStatus(true, "Aucun domaine connecté : le rapport RSoP nécessite une connexion.");
                return;
            }

            var result = await _gpoResult.GetResultantSetAsync(_computer.Name, context);
            if (result.IsSuccess && result.Value is not null)
            {
                await _inventory.SaveRsopReportAsync(result.Value, _computer.DistinguishedName);
                await LoadStoredRsopAsync(_computer.DistinguishedName);
                SetStatus(false, "Rapport RSoP obtenu sur " + _computer.Name
                    + " : " + result.Value.AppliedGpoNames.Count + " GPO appliquée(s) (" + result.Value.ReportDateUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + ").");
            }
            else if (result.Error is not null)
            {
                SetStatus(true, result.Error.ToDisplayString());
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Rapport RSoP échoué", ex);
            SetStatus(true, "Erreur lors du rapport RSoP : " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadHistoryAsync(string computerName)
    {
        try
        {
            var entries = await _audit.GetRecentAsync(500);
            var relevant = entries
                .Where(entry => entry.Target.Contains(computerName, StringComparison.OrdinalIgnoreCase)
                    || (entry.Details?.Contains(computerName, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (entry.Message?.Contains(computerName, StringComparison.OrdinalIgnoreCase) ?? false))
                .Take(20);

            History.Clear();
            foreach (var entry in relevant)
            {
                History.Add(new AuditEntryViewModel(entry));
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Lecture de l'historique échouée", ex);
        }
    }

    private void SetStatus(bool isError, string message)
    {
        IsStatusError = isError;
        StatusMessage = message;
        ShowStatus = !string.IsNullOrWhiteSpace(message);
    }
}