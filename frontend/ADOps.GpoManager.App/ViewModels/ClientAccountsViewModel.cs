using System.Collections.ObjectModel;
using System.IO;
using ADOps.GpoManager.App.Services;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Services;
using ADOps.GpoManager.Infrastructure.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ADOps.GpoManager.App.ViewModels;

/// <summary>
/// Vue « Comptes clients » : les comptes utilisateurs du domaine lus lors de
/// la synchronisation, avec leur état, leur département et leur niveau de
/// privilège (administrateur / standard). Elle donne aussi accès aux outils
/// d'administration Windows installés sur la machine (console ADUC, gestion de
/// l'ordinateur, stratégies de groupe) : le compte sélectionné sert de point
/// de départ pour aller vérifier l'objet dans la console native.
/// </summary>
public sealed partial class ClientAccountsViewModel : ViewModelBase
{
    private readonly InventoryQueryRepository _inventory;
    private readonly AdminToolsService _adminTools;
    private readonly ConnectionState _connection;
    private readonly ILogger _logger;

    /// <summary>
    /// Vrai une fois le constructeur terminé. Les callbacks de changement de
    /// propriété ne chargent les données qu'après cette initialisation : le
    /// générateur de source peut initialiser les propriétés avant l'affectation
    /// des dépendances.
    /// </summary>
    private bool _ready;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string? _selectedDepartment;

    [ObservableProperty]
    private bool _onlyStandardAccounts = true;

    [ObservableProperty]
    private ClientAccountRow? _selectedAccount;

    [ObservableProperty]
    private string _accountCountText = "0 compte";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _showStatus;

    [ObservableProperty]
    private bool _isStatusError;

    [ObservableProperty]
    private string _lastSyncText = "Aucune synchronisation";

    public ObservableCollection<ClientAccountRow> Accounts { get; } = new();

    public ObservableCollection<string> Departments { get; } = new();

    public ObservableCollection<AdminToolInfo> AdminTools { get; } = new();

    public ObservableCollection<string> InstalledShortcuts { get; } = new();

    public string AdministrativeToolsFolderText =>
        _adminTools.AdministrativeToolsFolder ?? "Dossier « Outils d'administration » introuvable sur cette machine.";

    public ConnectionState Connection => _connection;

    public ClientAccountsViewModel(
        InventoryQueryRepository inventory,
        AdminToolsService adminTools,
        ConnectionState connection,
        ILogger logger)
    {
        _inventory = inventory;
        _adminTools = adminTools;
        _connection = connection;
        _logger = logger;
        _ready = true;
    }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        LoadAdminTools();
        _ = LoadAccountsAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        if (_ready)
        {
            _ = LoadAccountsAsync();
        }
    }

    partial void OnSelectedDepartmentChanged(string? value)
    {
        if (_ready)
        {
            _ = LoadAccountsAsync();
        }
    }

    partial void OnOnlyStandardAccountsChanged(bool value)
    {
        if (_ready)
        {
            _ = LoadAccountsAsync();
        }
    }

    /// <summary>Recharge le cache des comptes (après une synchronisation).</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadAccountsAsync().ConfigureAwait(true);
    }

    private async Task LoadAccountsAsync()
    {
        try
        {
            var departments = await _inventory.GetClientAccountDepartmentsAsync().ConfigureAwait(true);
            Departments.Clear();
            foreach (var department in departments)
            {
                Departments.Add(department);
            }

            var accounts = await _inventory.GetClientAccountsAsync(
                SearchText,
                SelectedDepartment,
                OnlyStandardAccounts).ConfigureAwait(true);

            Accounts.Clear();
            foreach (var account in accounts)
            {
                Accounts.Add(account);
            }

            AccountCountText = Accounts.Count == 1
                ? "1 compte client"
                : Accounts.Count + " comptes clients";
        }
        catch (Exception ex)
        {
            _logger.Error("Chargement des comptes clients échoué", ex);
            AccountCountText = "Erreur de chargement";
            SetStatus(true, "Impossible de lire les comptes clients : " + ex.Message);
        }
    }

    private void LoadAdminTools()
    {
        AdminTools.Clear();
        foreach (var tool in _adminTools.GetTools())
        {
            AdminTools.Add(tool);
        }

        InstalledShortcuts.Clear();
        foreach (var shortcut in _adminTools.GetInstalledShortcuts())
        {
            InstalledShortcuts.Add(shortcut);
        }
    }

    /// <summary>Ouvre la console ADUC sur le contrôleur de domaine de la session.</summary>
    [RelayCommand]
    private void OpenActiveDirectoryUsers()
    {
        var aduc = AdminTools.FirstOrDefault(tool => tool.Descriptor.Id == "aduc");
        if (aduc is null)
        {
            SetStatus(true, "La console « Utilisateurs et ordinateurs Active Directory » n'est pas disponible.");
            return;
        }

        LaunchTool(aduc);
    }

    /// <summary>Lance l'outil d'administration sélectionné.</summary>
    [RelayCommand]
    private void LaunchTool(AdminToolInfo tool)
    {
        var controller = _connection.Current?.ServerFqdn;
        if (_adminTools.Launch(tool.Descriptor, controller, out var error))
        {
            SetStatus(false, "« " + tool.Descriptor.DisplayName + " » lancé sur "
                + (string.IsNullOrWhiteSpace(controller) ? "le poste local" : controller) + ".");
            return;
        }

        SetStatus(true, error);
    }

    /// <summary>Ouvre le dossier « Outils d'administration » de la machine.</summary>
    [RelayCommand]
    private void OpenAdministrativeToolsFolder()
    {
        if (_adminTools.OpenAdministrativeToolsFolder(out var error))
        {
            SetStatus(false, "Dossier « Outils d'administration » ouvert.");
            return;
        }

        SetStatus(true, error);
    }

    /// <summary>Ouvre un raccourci du dossier « Outils d'administration ».</summary>
    [RelayCommand]
    private void OpenShortcut(string shortcutName)
    {
        var folder = _adminTools.AdministrativeToolsFolder;
        if (folder is null)
        {
            SetStatus(true, "Dossier « Outils d'administration » introuvable.");
            return;
        }

        var path = Path.Combine(folder, shortcutName + ".lnk");
        if (!File.Exists(path))
        {
            path = Path.Combine(folder, shortcutName + ".msc");
        }

        if (_adminTools.LaunchShortcut(path, out var error))
        {
            SetStatus(false, "« " + shortcutName + " » lancé.");
            return;
        }

        SetStatus(true, error);
    }

    private void SetStatus(bool isError, string message)
    {
        IsStatusError = isError;
        StatusMessage = message;
        ShowStatus = !string.IsNullOrWhiteSpace(message);
    }
}
