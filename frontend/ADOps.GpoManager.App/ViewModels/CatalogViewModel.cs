using System.Collections.ObjectModel;
using ADOps.GpoManager.App.Services;
using ADOps.GpoManager.Core.Commands;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Services;
using ADOps.GpoManager.PowerShell.Host;
using ADOps.GpoManager.Templates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ADOps.GpoManager.App.ViewModels;

/// <summary>
/// Catalogue de modèles de stratégies (JSON versionnés). Un modèle n'est
/// PAS une GPO du domaine : il ne devient une GPO qu'après création
/// explicite sur le domaine (assistant de déploiement — prochaine étape).
/// </summary>
public sealed partial class CatalogViewModel : ViewModelBase
{
    private readonly IGpoCatalogService _catalog;
    private readonly IGpoScriptCatalog _scriptCatalog;
    private readonly IPowerShellExecutorFactory _executorFactory;
    private readonly ConnectionState _connection;
    private readonly IAuditService _audit;
    private readonly ILogger _logger;

    [ObservableProperty]
    private PolicyTemplate? _selectedTemplate;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _showStatus;

    [ObservableProperty]
    private bool _isStatusError;

    [ObservableProperty]
    private bool _isApplying;

    public ObservableCollection<PolicyTemplate> Templates { get; } = new();

    public string CatalogVersion => "Schéma v" + TemplateCatalog.CurrentSchemaVersion;

    public CatalogViewModel(
        IGpoCatalogService catalog,
        IGpoScriptCatalog scriptCatalog,
        IPowerShellExecutorFactory executorFactory,
        ConnectionState connection,
        IAuditService audit,
        ILogger logger)
    {
        _catalog = catalog;
        _scriptCatalog = scriptCatalog;
        _executorFactory = executorFactory;
        _connection = connection;
        _audit = audit;
        _logger = logger;
    }

    [RelayCommand]
    private async Task CreateAndApplyAsync(PolicyTemplate? template)
    {
        if (template is null) { return; }
        if (!_connection.IsConnected || _connection.Current is null)
        {
            SetStatus(true, "Aucun domaine connecté : impossible de créer la GPO.");
            return;
        }

        var script = _scriptCatalog.GetFullScript(template.TemplateId);
        if (string.IsNullOrWhiteSpace(script))
        {
            SetStatus(true, "Aucun script PowerShell pour le modèle « " + template.DisplayName + " ».");
            return;
        }

        IsApplying = true;
        SetStatus(false, "Création et application de « " + template.DisplayName + " » en cours…");

        try
        {
            var context = _connection.Current;
            var executor = context.ExecutorMode == ExecutorMode.Remote && context.Credentials is not null
                ? _executorFactory.CreateRemote(context.ServerFqdn, context.Credentials)
                : _executorFactory.Local;

            // SimplePowerShellCommand permet d'exécuter un script libre validé.
            var command = new SimplePowerShellCommand("CreateGpo_" + template.TemplateId, script, TimeSpan.FromMinutes(5));
            var result = await executor.ExecuteAsync(command);

            if (result.ExitCode == 0 && !result.TimedOut)
            {
                SetStatus(false, "GPO « " + template.DisplayName + " » créée et appliquée avec succès.\n" + result.StandardOutput);
                await _audit.LogAsync(
                    AuditActions.GpoCreate,
                    context.ServerFqdn,
                    AuditResult.Success,
                    "GPO créée depuis le catalogue : " + template.DisplayName,
                    "Modèle : " + template.TemplateId,
                    new AuditContext(
                        Server: context.ServerFqdn,
                        Domain: context.DomainFqdn,
                        Targets: template.TemplateId,
                        RequestedState: "GPO créée et liée",
                        OperationId: Guid.NewGuid().ToString("N")));
            }
            else
            {
                var err = string.IsNullOrWhiteSpace(result.StandardError) ? "Échec de la création de la GPO." : result.StandardError;
                SetStatus(true, err);
                await _audit.LogAsync(
                    AuditActions.GpoCreate,
                    context.ServerFqdn,
                    AuditResult.Failure,
                    "Échec de création de GPO : " + template.DisplayName,
                    err,
                    new AuditContext(
                        Server: context.ServerFqdn,
                        Domain: context.DomainFqdn,
                        Targets: template.TemplateId,
                        ErrorCode: "ExitCode=" + result.ExitCode,
                        OperationId: Guid.NewGuid().ToString("N")));
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Création de GPO depuis le catalogue échouée", ex);
            SetStatus(true, "Erreur : " + ex.Message);
        }
        finally
        {
            IsApplying = false;
        }
    }

    private void SetStatus(bool isError, string message)
    {
        IsStatusError = isError;
        StatusMessage = message;
        ShowStatus = !string.IsNullOrWhiteSpace(message);
    }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        Templates.Clear();
        foreach (var template in _catalog.GetTemplates())
        {
            Templates.Add(template);
        }
    }

    /// <summary>Recherche : filtre les modèles affichés (nom, catégorie, description).</summary>
    public bool MatchesSearch(PolicyTemplate template)
        => string.IsNullOrWhiteSpace(SearchText)
            || template.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || template.Category.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || template.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase);

    public string SupportStatusText(PolicyTemplate template) => template.SupportStatus switch
    {
        TemplateSupportStatus.Available => "Disponible",
        TemplateSupportStatus.MissingPrerequisite => "Prérequis manquant",
        TemplateSupportStatus.NotSupported => "Non pris en charge",
        TemplateSupportStatus.ComingSoon => "À venir",
        _ => "Inconnu",
    };
}
