using System.Collections.ObjectModel;
using ADOps.GpoManager.App.Services;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Services;
using ADOps.GpoManager.Infrastructure.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ADOps.GpoManager.App.ViewModels;

/// <summary>
/// Parc informatique : départements (issus du mapping), ordinateurs lus dans AD DS,
/// recherche et filtres. Les tests de disponibilité sont explicites (DNS + WinRM) :
/// un ping échoué ne prouve pas qu'une machine est éteinte.
/// </summary>
public sealed partial class ParcViewModel : ViewModelBase
{
    private readonly InventoryQueryRepository _inventory;
    private readonly ComputerHealthRepository _healthRepository;
    private readonly IComputerAvailabilityService _availability;
    private readonly IInventorySyncService _sync;
    private readonly INavigationService _navigation;
    private readonly ILogger _logger;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string? _selectedDepartment;

    [ObservableProperty]
    private string? _selectedAvailability;

    [ObservableProperty]
    private ComputerRow? _selectedComputer;

    [ObservableProperty]
    private string _computerCountText = "0 ordinateur";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _showStatus;

    [ObservableProperty]
    private bool _isStatusError;

    public ObservableCollection<DepartmentMapping> Departments { get; } = new();

    public ObservableCollection<ComputerRow> Computers { get; } = new();

    public IReadOnlyList<string> AvailabilityFilters { get; } = new[]
    {
        "En ligne (WinRM)",
        "Joignable (DNS), WinRM indisponible",
        "DNS non résolu",
        "Non vérifié",
    };

    public ConnectionState Connection { get; }

    public ParcViewModel(
        InventoryQueryRepository inventory,
        ComputerHealthRepository healthRepository,
        IComputerAvailabilityService availability,
        IInventorySyncService sync,
        INavigationService navigation,
        ILogger logger,
        ConnectionState connection)
    {
        _inventory = inventory;
        _healthRepository = healthRepository;
        _availability = availability;
        _sync = sync;
        _navigation = navigation;
        _logger = logger;
        Connection = connection;
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
            var departments = await _inventory.GetDepartmentsAsync();
            Departments.Clear();
            foreach (var department in departments)
            {
                Departments.Add(department);
            }

            await ApplyFiltersAsync();
        }
        catch (Exception ex)
        {
            _logger.Error("Chargement du parc échoué", ex);
            SetStatus(true, "Erreur de chargement du parc : " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyFiltersAsync()
    {
        try
        {
            var stateFilter = MapAvailabilityToState(SelectedAvailability);
            var computers = await _inventory.GetComputersAsync(
                string.IsNullOrWhiteSpace(SelectedDepartment) ? null : SelectedDepartment,
                string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
                stateFilter);

            Computers.Clear();
            foreach (var computer in computers)
            {
                Computers.Add(computer);
            }

            ComputerCountText = computers.Count switch
            {
                0 => "0 ordinateur",
                1 => "1 ordinateur",
                _ => computers.Count + " ordinateurs",
            };
        }
        catch (Exception ex)
        {
            _logger.Error("Filtrage du parc échoué", ex);
        }
    }

    private static string? MapAvailabilityToState(string? display)
        => display switch
        {
            "En ligne (WinRM)" => "Online",
            "Joignable (DNS), WinRM indisponible" => "OnlineWithoutWinRm",
            "DNS non résolu" => "DnsUnresolved",
            "Non vérifié" => "Unknown",
            _ => null,
        };

    /// <summary>Test de disponibilité des ordinateurs affichés : DNS puis WinRM.</summary>
    [RelayCommand]
    private async Task CheckAvailabilityAsync()
    {
        if (!Connection.IsConnected || Connection.Current is null)
        {
            SetStatus(true, "Aucun domaine connecté : les tests de disponibilité nécessitent une connexion.");
            return;
        }

        if (Computers.Count == 0)
        {
            SetStatus(true, "Aucun ordinateur affiché à tester. Synchronisez d'abord le parc.");
            return;
        }

        IsBusy = true;
        SetStatus(false, string.Empty);

        try
        {
            var targets = Computers
                .Select(row => new ComputerInfo
                {
                    Name = row.Name,
                    DistinguishedName = row.DistinguishedName,
                    OuDistinguishedName = row.OuDistinguishedName,
                    OperatingSystem = row.OperatingSystem,
                    OperatingSystemVersion = row.OperatingSystemVersion,
                    Enabled = row.Enabled,
                    LastLogonUtc = row.LastLogonUtc,
                    IpAddress = row.AdIpAddress,
                })
                .ToList();

            var results = await _availability.CheckAsync(targets, Connection.Current);
            await _healthRepository.SaveHealthAsync(results);

            await ApplyFiltersAsync();

            var online = results.Count(r => r.State is AvailabilityState.Online or AvailabilityState.OnlineWithoutWinRm);
            SetStatus(false, online + " ordinateur(s) joignable(s) sur " + results.Count
                + " testé(s) (DNS + WinRM). Les machines non résolues par le DNS peuvent néanmoins fonctionner : ICMP n'est pas utilisé comme preuve.");
        }
        catch (OperationCanceledException)
        {
            SetStatus(true, "Test de disponibilité annulé.");
        }
        catch (Exception ex)
        {
            _logger.Error("Tests de disponibilité échoués", ex);
            SetStatus(true, "Erreur lors des tests de disponibilité : " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SynchronizeAsync()
    {
        if (!Connection.IsConnected || Connection.Current is null)
        {
            SetStatus(true, "Aucun domaine connecté : la synchronisation nécessite une connexion.");
            return;
        }

        IsBusy = true;
        try
        {
            var summary = await _sync.SyncAsync(Connection.Current);
            Connection.SetSyncResult(summary);
            await LoadDataAsync();
            SetStatus(summary.State == SyncState.Failed,
                summary.State == SyncState.Succeeded
                    ? "Synchronisation réussie : " + summary.ComputersRead + " ordinateurs lus dans AD DS."
                    : summary.State == SyncState.Partial
                        ? "Synchronisation partielle : " + string.Join(" | ", summary.Warnings)
                        : "Échec de la synchronisation : " + (summary.Error ?? "erreur inconnue"));
        }
        catch (Exception ex)
        {
            _logger.Error("Synchronisation échouée", ex);
            SetStatus(true, "Erreur de synchronisation : " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenDepartmentMapping() => _navigation.NavigateTo<DepartmentMappingViewModel>();

    [RelayCommand]
    private void OpenComputerDetail()
    {
        if (_selectedComputer is not null)
        {
            _navigation.NavigateTo<ComputerDetailViewModel>(vm => vm.LoadComputer(_selectedComputer));
        }
    }

    private void SetStatus(bool isError, string message)
    {
        IsStatusError = isError;
        StatusMessage = message;
        ShowStatus = !string.IsNullOrWhiteSpace(message);
    }
}