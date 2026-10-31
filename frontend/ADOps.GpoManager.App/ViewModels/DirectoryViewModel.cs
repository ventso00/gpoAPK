using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using ADOps.GpoManager.App.Services;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Services;
using ADOps.GpoManager.Infrastructure.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ADOps.GpoManager.App.ViewModels;

/// <summary>
/// Annuaire AD DS : arborescence des unités d'organisation
/// (structure de référence : SARYTANY &gt; Administratif [Comptabilité,
/// Direction, Informatique, Logistique], Production [DAO, Lidar,
/// OrthoPhoto, Photogrammétrie, RoadScanning]) avec les utilisateurs
/// rattachés à chaque OU, et fiche de détail par utilisateur.
/// AD DS reste la source de vérité : les OUs proviennent du cache
/// de synchronisation local, les utilisateurs sont lus en direct.
/// </summary>
public sealed partial class DirectoryViewModel : ViewModelBase
{
    private readonly IDirectoryService _directory;
    private readonly InventoryQueryRepository _inventory;
    private readonly IInventorySyncService _sync;
    private readonly ILogger _logger;

    /// <summary>Racine de l'arborescence : le domaine lui-même.</summary>
    public OuNode Root { get; } = new(string.Empty, string.Empty, null, 0);

    public ObservableCollection<UserInfo> OuUsers { get; } = new();
    public ObservableCollection<GpoLinkInfo> OuGpoLinks { get; } = new();

    private readonly ICollectionView _ouUsersView;

    [ObservableProperty]
    private OuNode? _selectedOu;

    [ObservableProperty]
    private UserInfo? _selectedUser;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _ouDetailsText = string.Empty;

    [ObservableProperty]
    private string _userCountText = "0 utilisateur";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _showStatus;

    [ObservableProperty]
    private bool _isStatusError;

    public string Filter
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                _ouUsersView.Refresh();
            }
        }
    }

    public DirectoryViewModel(
        IDirectoryService directory,
        InventoryQueryRepository inventory,
        IInventorySyncService sync,
        ConnectionState connection,
        ILogger logger)
    {
        _directory = directory;
        _inventory = inventory;
        _sync = sync;
        Connection = connection;
        _logger = logger;
        _ouUsersView = CollectionViewSource.GetDefaultView(OuUsers);
        _ouUsersView.Filter = FilterUser;
    }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        _ = LoadDirectoryAsync();
    }

    private async Task LoadDirectoryAsync()
    {
        if (!ConnectionStateIsConnected(out var context))
        {
            SetStatus(true, "Aucun domaine connecté : l'annuaire nécessite une connexion (page de connexion).");
            return;
        }

        IsBusy = true;
        SetStatus(false, string.Empty);

        try
        {
            // 1. OUs depuis le cache local (peuplé par une synchronisation)
            var ouRows = await _inventory.GetOuMappingsAsync();
            if (ouRows.Count == 0)
            {
                var summary = await _sync.SyncAsync(context!);
                Connection.SetSyncResult(summary);
                ouRows = await _inventory.GetOuMappingsAsync();
            }

            // 2. Utilisateurs en direct depuis AD DS
            var usersResult = await _directory.ListUsersAsync(context!);
            if (!usersResult.IsSuccess)
            {
                SetStatus(true, usersResult.Error?.ToDisplayString()
                    ?? "Lecture des utilisateurs impossible.");
                return;
            }

            var users = usersResult.Value ?? Array.Empty<UserInfo>();

            // 3. Liens de GPO du cache local (pour le panneau de l'OU sélectionnée)
            var allLinks = await _inventory.GetAllLinksAsync();

            BuildTree(ouRows, users, context!.DomainFqdn);
            _cachedLinks = allLinks;

            SetStatus(false, users.Count + " utilisateur(s) et " + ouRows.Count
                + " OU chargé(s) depuis " + context.DomainFqdn
                + ". Sélectionnez une OU pour afficher ses utilisateurs et ses GPO liées.");
        }
        catch (Exception ex)
        {
            _logger.Error("Chargement de l'annuaire échoué", ex);
            SetStatus(true, "Erreur de chargement de l'annuaire : " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private IReadOnlyList<GpoLinkInfo> _cachedLinks = Array.Empty<GpoLinkInfo>();

    public ConnectionState Connection { get; }

    /// <summary>
    /// Construit l'arborescence : nœud domaine → OUs (hiérarchie
    /// parent/enfant par DistinguishedName) → utilisateurs rattachés.
    /// Les comptes hors OU (ex. : CN=Users) forment des nœuds "Autres".
    /// </summary>
    private void BuildTree(
        IReadOnlyList<OuMappingRow> ouRows,
        IReadOnlyList<UserInfo> users,
        string domainFqdn)
    {
        Root.Name = domainFqdn;
        Root.DistinguishedName = ExtractDomainDn(ouRows.FirstOrDefault()?.DistinguishedName) ?? string.Empty;
        Root.Children.Clear();
        Root.Users.Clear();
        Root.Items.Clear();

        var nodesByDn = new Dictionary<string, OuNode>(StringComparer.OrdinalIgnoreCase)
        {
            [Root.DistinguishedName] = Root,
        };

        // Nœuds d'OU
        foreach (var row in ouRows)
        {
            var node = new OuNode(row.Name, row.DistinguishedName, DeriveParentDn(row.DistinguishedName), row.ComputerCount);
            nodesByDn[row.DistinguishedName] = node;
        }

        // Hiérarchie parent → enfant
        foreach (var node in nodesByDn.Values.Where(node => node != Root))
        {
            var parentDn = node.ParentDistinguishedName;
            if (!string.IsNullOrWhiteSpace(parentDn)
                && nodesByDn.TryGetValue(parentDn, out var parent))
            {
                parent.Children.Add(node);
            }
            else
            {
                Root.Children.Add(node);
            }
        }

        // Utilisateurs rattachés à leur OU parente
        var orphanUsers = new List<UserInfo>();
        foreach (var user in users)
        {
            var ouDn = user.OuDistinguishedName;
            if (!string.IsNullOrWhiteSpace(ouDn) && nodesByDn.TryGetValue(ouDn, out var node))
            {
                node.Users.Add(user);
            }
            else
            {
                orphanUsers.Add(user);
            }
        }

        // Comptes hors OU (CN=Users, ...) : nœud par conteneur parent
        foreach (var group in orphanUsers.GroupBy(user => user.OuDistinguishedName ?? string.Empty))
        {
            var containerDn = group.Key;
            var containerName = string.IsNullOrWhiteSpace(containerDn)
                ? "Autres comptes"
                : LastRdnLabel(containerDn);
            var container = new OuNode(containerName, containerDn, null, 0);
            foreach (var user in group)
            {
                container.Users.Add(user);
            }

            Root.Children.Add(container);
            nodesByDn[containerDn] = container;
        }

        // Compteurs de sous-arbre + éléments du TreeView (enfants puis utilisateurs)
        ComputeSubtreeCounts(Root);
        foreach (var node in nodesByDn.Values)
        {
            foreach (var child in node.Children.OrderBy(child => child.Name, StringComparer.OrdinalIgnoreCase))
            {
                node.Items.Add(child);
            }

            foreach (var user in node.Users.OrderBy(user => user.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                node.Items.Add(user);
            }
        }

        // Sélection par défaut : la racine
        SelectedOu = Root;
    }

    partial void OnSelectedOuChanged(OuNode? value)
    {
        if (value is null)
        {
            return;
        }

        OuUsers.Clear();
        foreach (var user in value.Users)
        {
            OuUsers.Add(user);
        }

        UserCountText = value.Users.Count switch
        {
            0 => "0 utilisateur",
            1 => "1 utilisateur",
            _ => value.Users.Count + " utilisateurs",
        };

        OuDetailsText = string.IsNullOrWhiteSpace(value.DistinguishedName)
            ? "Racine du domaine"
            : value.DistinguishedName;

        // Liens de GPO de l'OU sélectionnée (cache local horodaté)
        OuGpoLinks.Clear();
        foreach (var link in _cachedLinks
                     .Where(link => string.Equals(link.TargetDistinguishedName, value.DistinguishedName, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(link => link.Order))
        {
            OuGpoLinks.Add(link);
        }

        _ouUsersView.Refresh();
    }

    partial void OnSelectedUserChanged(UserInfo? value)
    {
        // La fiche utilisateur est liée directement dans la vue (SelectedUser).
    }

    private bool FilterUser(object item)
    {
        if (item is not UserInfo user)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(Filter))
        {
            return true;
        }

        return user.DisplayName.Contains(Filter, StringComparison.OrdinalIgnoreCase)
            || user.SamAccountName.Contains(Filter, StringComparison.OrdinalIgnoreCase)
            || (user.Email?.Contains(Filter, StringComparison.OrdinalIgnoreCase) ?? false)
            || (user.Department?.Contains(Filter, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    /// <summary>DN du domaine : suffixe composé uniquement de composants DC=.</summary>
    private static string? ExtractDomainDn(string? distinguishedName)
    {
        if (string.IsNullOrWhiteSpace(distinguishedName))
        {
            return null;
        }

        var components = distinguishedName.Split(',');
        var domainComponents = new List<string>();
        for (var index = components.Length - 1; index >= 0; index--)
        {
            if (components[index].StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
            {
                domainComponents.Insert(0, components[index]);
            }
            else
            {
                break;
            }
        }

        return domainComponents.Count > 0 ? string.Join(",", domainComponents) : null;
    }

    /// <summary>Le parent d'un DN est le DN sans son premier composant RDN.</summary>
    private static string? DeriveParentDn(string distinguishedName)
    {
        var commaIndex = distinguishedName.IndexOf(',');
        return commaIndex > 0 && commaIndex < distinguishedName.Length - 1
            ? distinguishedName[(commaIndex + 1)..]
            : null;
    }

    /// <summary>Dernière étiquette RDN (ex. : « Users » depuis « CN=Users,DC=… »).</summary>
    private static string LastRdnLabel(string distinguishedName)
    {
        var first = distinguishedName.Split(',')[0];
        var separator = first.IndexOf('=');
        return separator >= 0 && separator < first.Length - 1
            ? first[(separator + 1)..]
            : first;
    }

    /// <summary>Compteurs récursifs : utilisateurs et ordinateurs du sous-arbre.</summary>
    private static (int Users, int Computers) ComputeSubtreeCounts(OuNode node)
    {
        var users = node.Users.Count;
        var computers = node.ComputerCount;
        foreach (var child in node.Children)
        {
            var (childUsers, childComputers) = ComputeSubtreeCounts(child);
            users += childUsers;
            computers += childComputers;
        }

        node.SubtreeUserCount = users;
        node.SubtreeComputerCount = computers;
        return (users, computers);
    }

    private bool ConnectionStateIsConnected([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out DomainContext? context)
    {
        context = Connection.Current;
        return Connection.IsConnected && context is not null;
    }

    private void SetStatus(bool isError, string message)
    {
        IsStatusError = isError;
        StatusMessage = message;
        ShowStatus = !string.IsNullOrWhiteSpace(message);
    }
}

/// <summary>
/// Nœud de l'arborescence annuaire : une unité d'organisation
/// (ou le domaine) avec ses sous-OU et ses utilisateurs directs.
/// <see cref="Items">Items</see> rassemble enfants et utilisateurs
/// pour le TreeView (le template est choisi par type de données).
/// </summary>
public sealed class OuNode : ObservableObject
{
    public OuNode(string name, string distinguishedName, string? parentDistinguishedName, int computerCount)
    {
        Name = name;
        DistinguishedName = distinguishedName;
        ParentDistinguishedName = parentDistinguishedName;
        ComputerCount = computerCount;
    }

    public string Name { get; set; }
    public string DistinguishedName { get; set; }
    public string? ParentDistinguishedName { get; }
    public int ComputerCount { get; }

    /// <summary>Sous-unités d'organisation.</summary>
    public ObservableCollection<OuNode> Children { get; } = new();

    /// <summary>Utilisateurs directement rattachés à cette OU.</summary>
    public ObservableCollection<UserInfo> Users { get; } = new();

    /// <summary>Éléments affichés dans le TreeView : sous-OUs puis utilisateurs.</summary>
    public ObservableCollection<object> Items { get; } = new();

    /// <summary>Utilisateurs du sous-arbre (cette OU incluse).</summary>
    public int SubtreeUserCount { get; set; }

    /// <summary>Ordinateurs du sous-arbre (cette OU incluse).</summary>
    public int SubtreeComputerCount { get; set; }

    public string SubtreeText => SubtreeUserCount + " utilisateur(s) · " + SubtreeComputerCount + " ordinateur(s) dans le sous-arbre";
}
