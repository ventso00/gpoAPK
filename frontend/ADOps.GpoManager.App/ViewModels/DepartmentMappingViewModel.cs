using System.Collections.ObjectModel;
using ADOps.GpoManager.App.Services;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Services;
using ADOps.GpoManager.Infrastructure.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ADOps.GpoManager.App.ViewModels;

/// <summary>
/// Correspondance département ↔ OU, stockée dans la base locale.
/// L'application ne crée, ne renomme et ne déplace jamais d'OU dans AD DS :
/// le mapping est une organisation locale de l'affichage et du ciblage.
/// </summary>
public sealed partial class DepartmentMappingViewModel : ViewModelBase
{
    private readonly InventoryQueryRepository _inventory;
    private readonly IDepartmentMappingService _mapping;
    private readonly IAuditService _audit;
    private readonly ILogger _logger;

    [ObservableProperty]
    private OuMappingRow? _selectedOu;

    [ObservableProperty]
    private string _departmentName = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _showStatus;

    [ObservableProperty]
    private bool _isStatusError;

    public ObservableCollection<OuMappingRow> OrganizationalUnits { get; } = new();

    public ObservableCollection<DepartmentMapping> Departments { get; } = new();

    public DepartmentMappingViewModel(
        InventoryQueryRepository inventory,
        IDepartmentMappingService mapping,
        IAuditService audit,
        ILogger logger)
    {
        _inventory = inventory;
        _mapping = mapping;
        _audit = audit;
        _logger = logger;
    }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        _ = LoadDataAsync();
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        IsBusy = true;
        try
        {
            var ous = await _inventory.GetOuMappingsAsync();
            OrganizationalUnits.Clear();
            foreach (var ou in ous)
            {
                OrganizationalUnits.Add(ou);
            }

            var departments = await _mapping.GetMappingsAsync();
            Departments.Clear();
            foreach (var department in departments)
            {
                Departments.Add(department);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Chargement du mapping échoué", ex);
            SetStatus(true, "Erreur de chargement : " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Assigne le département saisi à l'OU sélectionnée (stockage local uniquement).</summary>
    [RelayCommand]
    private async Task AssignDepartmentAsync()
    {
        if (_selectedOu is null)
        {
            SetStatus(true, "Sélectionnez d'abord une unité d'organisation dans la liste.");
            return;
        }

        if (string.IsNullOrWhiteSpace(DepartmentName))
        {
            SetStatus(true, "Saisissez un nom de département (ex. : Informatique, Direction, Commercial).");
            return;
        }

        IsBusy = true;
        try
        {
            var name = DepartmentName.Trim();
            await _mapping.SaveMappingAsync(name, _selectedOu.DistinguishedName);

            await _audit.LogAsync(
                AuditActions.DepartmentMapping,
                _selectedOu.DistinguishedName,
                AuditResult.Success,
                "Département « " + name + " » associé à l'OU « " + _selectedOu.Name + " » (" + _selectedOu.ComputerCount + " ordinateur(s)).",
                context: new AuditContext(
                    Targets: _selectedOu.DistinguishedName,
                    RequestedState: name,
                    OperationId: System.Guid.NewGuid().ToString("N")));

            DepartmentName = string.Empty;
            await LoadDataAsync();
            SetStatus(false, "Association enregistrée localement : les ordinateurs de « " + _selectedOu.Name + " » apparaissent dans le département « " + name + " ». AD DS n'a pas été modifié.");
        }
        catch (Exception ex)
        {
            _logger.Error("Association échouée", ex);
            SetStatus(true, "Erreur lors de l'association : " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RemoveMappingAsync(DepartmentMapping? mapping)
    {
        if (mapping is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _mapping.RemoveMappingAsync(mapping.DepartmentName);

            await _audit.LogAsync(
                AuditActions.DepartmentMapping,
                mapping.OuDistinguishedName ?? mapping.DepartmentName,
                AuditResult.Information,
                "Mapping du département « " + mapping.DepartmentName + " » supprimé. Les ordinateurs concernés redeviennent « Non classé ».",
                context: new AuditContext(
                    Targets: mapping.OuDistinguishedName,
                    OperationId: System.Guid.NewGuid().ToString("N")));

            await LoadDataAsync();
            SetStatus(false, "Mapping supprimé. AD DS n'a pas été modifié.");
        }
        catch (Exception ex)
        {
            _logger.Error("Suppression du mapping échouée", ex);
            SetStatus(true, "Erreur lors de la suppression : " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetStatus(bool isError, string message)
    {
        IsStatusError = isError;
        StatusMessage = message;
        ShowStatus = !string.IsNullOrWhiteSpace(message);
    }
}