using System.Collections.ObjectModel;
using ADOps.GpoManager.App.Services;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ADOps.GpoManager.App.ViewModels;

public sealed partial class AuditViewModel : ViewModelBase
{
    private readonly IAuditService _audit;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ObservableCollection<AuditEntryViewModel> Entries { get; } = new();

    public AuditViewModel(IAuditService audit)
    {
        _audit = audit;
    }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var entries = await _audit.GetRecentAsync(500);
            Entries.Clear();
            foreach (var entry in entries.Reverse())
            {
                Entries.Add(new AuditEntryViewModel(entry));
            }

            StatusMessage = entries.Count + " entrée(s) d'audit affichée(s) — plus récentes en premier.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Erreur de lecture de l'audit : " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>Adaptateur d'affichage d'une entrée d'audit.</summary>
public sealed class AuditEntryViewModel
{
    public long Id { get; }
    public string Timestamp { get; }
    public string UserName { get; }
    public string Action { get; }
    public string Target { get; }
    public AuditResult Result { get; }
    public string ResultText { get; }
    public string Message { get; }
    public string Details { get; }
    public string Server { get; }
    public string Domain { get; }
    public string ObjectGuid { get; }
    public string Targets { get; }
    public string InitialState { get; }
    public string RequestedState { get; }
    public string ErrorCode { get; }
    public string? DurationText { get; }
    public string? OperationId { get; }
    public string MetaText { get; }
    public string TooltipText { get; }

    public AuditEntryViewModel(AuditEntry entry)
    {
        Id = entry.Id;
        Timestamp = entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        UserName = entry.UserName;
        Action = entry.Action;
        Target = entry.Target;
        Result = entry.Result;
        ResultText = entry.Result switch
        {
            AuditResult.Success => "Succès",
            AuditResult.Warning => "Attention",
            AuditResult.Failure => "Échec",
            _ => "Info",
        };
        Message = entry.Message;
        Details = entry.Details ?? string.Empty;
        Server = entry.Server ?? string.Empty;
        Domain = entry.Domain ?? string.Empty;
        ObjectGuid = entry.ObjectGuid ?? string.Empty;
        Targets = entry.Targets ?? string.Empty;
        InitialState = entry.InitialState ?? string.Empty;
        RequestedState = entry.RequestedState ?? string.Empty;
        ErrorCode = entry.ErrorCode ?? string.Empty;
        DurationText = entry.Duration is null ? null : entry.Duration.Value.TotalMilliseconds < 1000
            ? entry.Duration.Value.TotalMilliseconds.ToString("0") + " ms"
            : entry.Duration.Value.TotalSeconds.ToString("0.0") + " s";
        OperationId = entry.OperationId;

        var meta = new List<string>();
        if (!string.IsNullOrWhiteSpace(UserName))
        {
            meta.Add(UserName);
        }

        if (!string.IsNullOrWhiteSpace(Server))
        {
            meta.Add(Server);
        }

        if (!string.IsNullOrWhiteSpace(DurationText))
        {
            meta.Add(DurationText);
        }

        MetaText = string.Join(" · ", meta);

        var tooltip = new List<string>
        {
            "Horodatage : " + Timestamp,
            "Utilisateur : " + (string.IsNullOrWhiteSpace(UserName) ? "—" : UserName),
            "Serveur : " + (string.IsNullOrWhiteSpace(Server) ? "—" : Server),
            "Domaine : " + (string.IsNullOrWhiteSpace(Domain) ? "—" : Domain),
            "Résultat : " + ResultText,
            "Durée : " + (string.IsNullOrWhiteSpace(DurationText) ? "—" : DurationText),
            "ID opération : " + (string.IsNullOrWhiteSpace(OperationId) ? "—" : OperationId),
        };
        if (!string.IsNullOrWhiteSpace(Details))
        {
            tooltip.Add(string.Empty);
            tooltip.Add(Details);
        }

        TooltipText = string.Join(Environment.NewLine, tooltip);
    }
}
