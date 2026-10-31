using System.Globalization;
using System.Text.Json;
using ADOps.GpoManager.App.Controls;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Services;
using ADOps.GpoManager.Infrastructure.Storage;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ADOps.GpoManager.App.ViewModels;

/// <summary>Élément de navigation latérale (une section de l'application).</summary>
public sealed partial class NavigationItemViewModel : ObservableObject
{
    public string Key { get; }
    public string Title { get; }
    public LucideIconName Icon { get; }

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private int _count;

    public bool ShowCount => Count > 0;

    public NavigationItemViewModel(string key, string title, LucideIconName icon)
    {
        Key = key;
        Title = title;
        Icon = icon;
    }

    partial void OnCountChanged(int value) => OnPropertyChanged(nameof(ShowCount));

    public void SetCount(int value) => Count = value;
}

/// <summary>Poste client sélectionnable, enrichi de ses traces de déploiement locales.</summary>
public sealed partial class MachineItemViewModel : ObservableObject
{
    private readonly Action<MachineItemViewModel> _selectionChanged;

    public ComputerRow Row { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private int _recordCount;

    [ObservableProperty]
    private bool _isCurrentClient;

    /// <summary>Adresse obtenue par une résolution DNS en direct (prioritaire sur le cache).</summary>
    [ObservableProperty]
    private string? _liveIpAddress;

    public string Name => Row.Name;
    public string Department => string.IsNullOrWhiteSpace(Row.DepartmentName)
        ? (string.IsNullOrWhiteSpace(Row.OuName) ? "Sans département" : Row.OuName!)
        : Row.DepartmentName!;
    public string OuText => string.IsNullOrWhiteSpace(Row.OuName) ? ShortOu(Row.OuDistinguishedName) : Row.OuName!;
    public string DistinguishedName => Row.DistinguishedName;
    public string IpText => !string.IsNullOrWhiteSpace(LiveIpAddress) ? LiveIpAddress!
        : Row.EffectiveIpAddress ?? "IP inconnue";
    public string OsText => string.IsNullOrWhiteSpace(Row.OperatingSystem) ? "Système inconnu" : Row.OperatingSystem!;
    public string LastLogonText => Row.LastLogonUtc is null
        ? "Jamais connecté"
        : "Dernière connexion : " + Row.LastLogonUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string StatusText => Row.AvailabilityState switch
    {
        AvailabilityState.Online => "En ligne",
        AvailabilityState.OnlineWithoutWinRm => "DNS seul",
        AvailabilityState.DnsUnresolved => "Hors ligne",
        _ => "Non vérifié",
    };
    public string StatusDetailText => Row.AvailabilityText;
    public bool IsServer => Row.OperatingSystem?.Contains("Server", StringComparison.OrdinalIgnoreCase) == true;
    public bool IsOnline => Row.WinRmAvailable;
    public bool IsOffline => Row.AvailabilityState == AvailabilityState.DnsUnresolved;
    public bool IsUnknown => Row.AvailabilityState == AvailabilityState.Unknown;
    public bool HasRecords => RecordCount > 0;
    public bool IsAccountEnabled => Row.Enabled;
    public bool IsAccountDisabled => !Row.Enabled;
    public string AccountStateText => Row.Enabled ? "Compte AD activé" : "Compte AD désactivé";
    public string AccountActionText => Row.Enabled ? "Désactiver le compte" : "Activer le compte";
    public string AccountStatePillText => Row.Enabled ? "Activé" : "Désactivé";

    public string RecordCountText => RecordCount switch
    {
        0 => "Aucune GPO ADOps",
        1 => "1 GPO ADOps",
        _ => RecordCount + " GPO ADOps",
    };

    public MachineItemViewModel(ComputerRow row, bool isSelected, Action<MachineItemViewModel> selectionChanged)
    {
        Row = row;
        _isSelected = isSelected;
        _selectionChanged = selectionChanged;
    }

    partial void OnIsSelectedChanged(bool value) => _selectionChanged(this);

    partial void OnLiveIpAddressChanged(string? value) => OnPropertyChanged(nameof(IpText));

    partial void OnRecordCountChanged(int value)
    {
        OnPropertyChanged(nameof(RecordCountText));
        OnPropertyChanged(nameof(HasRecords));
    }

    public ComputerInfo ToComputerInfo() => new()
    {
        Name = Row.Name,
        DistinguishedName = Row.DistinguishedName,
        SamAccountName = Row.Name + "$",
        OuDistinguishedName = Row.OuDistinguishedName,
        OperatingSystem = Row.OperatingSystem,
        OperatingSystemVersion = Row.OperatingSystemVersion,
        Enabled = Row.Enabled,
        LastLogonUtc = Row.LastLogonUtc,
    };

    private static string ShortOu(string dn)
    {
        if (string.IsNullOrWhiteSpace(dn))
        {
            return "OU inconnue";
        }

        var first = dn.Split(',', 2)[0];
        var index = first.IndexOf('=');
        return index >= 0 && index + 1 < first.Length ? first[(index + 1)..] : dn;
    }
}

/// <summary>Département = regroupement d'après l'OU réelle des postes.</summary>
public sealed partial class DepartmentGroupViewModel : ObservableObject
{
    public string Name { get; }
    public string Subtitle { get; }
    public IReadOnlyList<MachineItemViewModel> Machines { get; }

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private string _selectionText = "0 / 0 sélectionné";

    [ObservableProperty]
    private int _onlineCount;

    public string Meta => Machines.Count + (Machines.Count > 1 ? " postes" : " poste")
        + " · " + OnlineCount + " en ligne";

    public bool IsAllSelected => Machines.Count > 0 && Machines.All(machine => machine.IsSelected);

    public DepartmentGroupViewModel(string name, string subtitle, IReadOnlyList<MachineItemViewModel> machines)
    {
        Name = name;
        Subtitle = subtitle;
        Machines = machines;
        Recount();
    }

    public void Recount()
    {
        var selected = Machines.Count(machine => machine.IsSelected);
        SelectionText = selected + " / " + Machines.Count + " sélectionné" + (selected > 1 ? "s" : string.Empty);
        OnlineCount = Machines.Count(machine => machine.IsOnline);
        OnPropertyChanged(nameof(Meta));
        OnPropertyChanged(nameof(IsAllSelected));
    }
}

/// <summary>Paramètre éditable d'un modèle (text, booléen, énumération, liste).</summary>
public sealed partial class ParameterFieldViewModel : ObservableObject
{
    private readonly Action _changed;
    private bool _synchronizing;

    public string Name { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public string Placeholder { get; }
    public TemplateParameterType Type { get; }
    public IReadOnlyList<string> Options { get; }

    [ObservableProperty]
    private string _value = string.Empty;

    [ObservableProperty]
    private bool _boolValue;

    public bool IsBoolean => Type == TemplateParameterType.Boolean;
    public bool IsEnum => Type == TemplateParameterType.Enum;
    public bool IsInteger => Type == TemplateParameterType.Integer;
    public bool IsMultiline => Type == TemplateParameterType.MultilineText;
    public bool IsText => Type is TemplateParameterType.Text or TemplateParameterType.Integer;

    public ParameterFieldViewModel(TemplateParameter parameter, string? initialValue, Action changed)
    {
        Name = parameter.Name;
        DisplayName = parameter.DisplayName;
        Description = parameter.Description ?? string.Empty;
        Placeholder = parameter.Placeholder ?? string.Empty;
        Type = parameter.Type;
        Options = parameter.EnumValues ?? Array.Empty<string>();
        _changed = changed;

        _value = initialValue
            ?? parameter.DefaultValue
            ?? (parameter.Type == TemplateParameterType.Enum ? Options.FirstOrDefault() ?? string.Empty : string.Empty);
        _boolValue = ParseBoolean(_value);
    }

    partial void OnValueChanged(string value)
    {
        if (_synchronizing)
        {
            return;
        }

        _synchronizing = true;
        BoolValue = ParseBoolean(value);
        _synchronizing = false;
        _changed();
    }

    partial void OnBoolValueChanged(bool value)
    {
        if (_synchronizing)
        {
            return;
        }

        _synchronizing = true;
        Value = value ? "true" : "false";
        _synchronizing = false;
        _changed();
    }

    private static bool ParseBoolean(string value)
        => value.Trim().ToLowerInvariant() is "true" or "1" or "yes" or "oui" or "activé" or "active";
}

/// <summary>Carte d'un modèle du catalogue avec sa sélection et ses paramètres.</summary>
public sealed partial class TemplateItemViewModel : ObservableObject
{
    private readonly IGpoCatalogService _catalog;
    private readonly Action _selectionChanged;
    private readonly Action _settingsChanged;

    public PolicyTemplate Template { get; }
    public IReadOnlyList<ParameterFieldViewModel> Parameters { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private int _settingCount;

    [ObservableProperty]
    private string? _previewError;

    public string TemplateId => Template.TemplateId;
    public string DisplayName => Template.DisplayName;
    public string Description => Template.Description;
    public string Category => Template.Category;
    public bool IsUserScope => Template.Scope == PolicyScope.User;
    public string ScopeText => IsUserScope ? "Utilisateur" : "Ordinateur";
    public bool IsHighRisk => string.Equals(Template.RiskLevel, "High", StringComparison.OrdinalIgnoreCase);
    public bool IsMediumRisk => string.Equals(Template.RiskLevel, "Medium", StringComparison.OrdinalIgnoreCase);
    public string RiskText => Template.RiskLevel switch
    {
        "High" => "Risque élevé",
        "Medium" => "Risque modéré",
        _ => "Risque faible",
    };
    public string ParameterCountText => Parameters.Count + (Parameters.Count > 1 ? " paramètres" : " paramètre");
    public string SearchBlob => string.Join(' ', new[]
    {
        DisplayName, Description, Category, ScopeText, RiskText,
    }).ToLowerInvariant();

    public TemplateItemViewModel(
        PolicyTemplate template,
        TemplateRegistryBinding? binding,
        IGpoCatalogService catalog,
        IReadOnlyDictionary<string, string>? previousValues,
        Action selectionChanged,
        Action settingsChanged)
    {
        Template = template;
        _catalog = catalog;
        _selectionChanged = selectionChanged;
        _settingsChanged = settingsChanged;

        var defaults = binding?.DefaultParameters ?? new Dictionary<string, string>();
        Parameters = template.Parameters
            .Select(parameter =>
            {
                defaults.TryGetValue(parameter.Name, out var fallback);
                string? previous = null;
                previousValues?.TryGetValue(parameter.Name, out previous);
                return new ParameterFieldViewModel(
                    parameter,
                    previous ?? fallback,
                    OnParameterChanged);
            })
            .ToList();

        Recalculate();
    }

    partial void OnIsSelectedChanged(bool value) => _selectionChanged();

    private void OnParameterChanged()
    {
        Recalculate();
        _settingsChanged();
    }

    public void Recalculate()
    {
        try
        {
            var settings = _catalog.Materialize(TemplateId, BuildParameters());
            SettingCount = settings.Count;
            PreviewError = null;
        }
        catch (Exception ex)
        {
            SettingCount = 0;
            PreviewError = ex.Message;
        }

        OnPropertyChanged(nameof(SettingsText));
        OnPropertyChanged(nameof(HasError));
    }

    public bool HasError => !string.IsNullOrWhiteSpace(PreviewError);

    public string SettingsText => SettingCount == 0
        ? "Aucune valeur — vérifiez les paramètres"
        : SettingCount + (SettingCount > 1 ? " valeurs de registre" : " valeur de registre");

    public IReadOnlyDictionary<string, object?> BuildParameters()
        => Parameters.ToDictionary(
            parameter => parameter.Name,
            parameter => (object?)parameter.Value,
            StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> ExportValues()
        => Parameters.ToDictionary(
            parameter => parameter.Name,
            parameter => parameter.Value,
            StringComparer.OrdinalIgnoreCase);
}

/// <summary>Filtre de catégorie du catalogue.</summary>
public sealed partial class CategoryFilterViewModel : ObservableObject
{
    public string Category { get; }
    public string Label { get; }

    [ObservableProperty]
    private bool _isActive;

    public CategoryFilterViewModel(string category, string label)
    {
        Category = category;
        Label = label;
    }
}

/// <summary>Unité d'organisation proposée comme destination d'un déplacement AD DS.</summary>
public sealed class OuOptionViewModel
{
    public string DistinguishedName { get; }
    public string Name { get; }

    public string DisplayName => string.IsNullOrEmpty(DistinguishedName)
        ? Name
        : Name + "  ·  " + DistinguishedName;

    public OuOptionViewModel(string distinguishedName, string name)
    {
        DistinguishedName = distinguishedName;
        Name = name;
    }

    public override string ToString() => DisplayName;
}

/// <summary>Ligne de terminal (déploiement, actualisation, vérification).</summary>
public sealed class ConsoleLineViewModel
{
    public string TimeText { get; init; } = DateTime.Now.ToString("HH:mm:ss");
    public string DisplayText { get; init; } = string.Empty;
    public string ColorHex { get; init; } = "#C9E3E0";

    public static ConsoleLineViewModel From(ExecutionLine line)
        => new()
        {
            TimeText = line.TimeText,
            DisplayText = line.DisplayText,
            ColorHex = line.Level switch
            {
                ExecutionLevel.Success => "#61D5A0",
                ExecutionLevel.Warning => "#FFC66D",
                ExecutionLevel.Error => "#FF777D",
                ExecutionLevel.Command => "#5CF0C4",
                _ => "#C9E3E0",
            },
        };

    public static ConsoleLineViewModel Info(string message)
        => new() { DisplayText = "[INFO]  " + message };

    public static ConsoleLineViewModel Success(string message)
        => new() { DisplayText = "[OK]    " + message, ColorHex = "#61D5A0" };

    public static ConsoleLineViewModel Warning(string message)
        => new() { DisplayText = "[AVERT] " + message, ColorHex = "#FFC66D" };

    public static ConsoleLineViewModel Error(string message)
        => new() { DisplayText = "[ERREUR] " + message, ColorHex = "#FF777D" };

    public static ConsoleLineViewModel Command(string message)
        => new() { DisplayText = "> " + message, ColorHex = "#5CF0C4" };
}

/// <summary>Résultat par poste après un déploiement.</summary>
public sealed class DeploymentResultRow
{
    public string MachineName { get; init; } = string.Empty;
    public bool IsSuccess { get; init; }
    public string StatusText => IsSuccess ? "Succès" : "Échec";
    public string Detail { get; init; } = string.Empty;
}

/// <summary>Avertissement du plan de déploiement.</summary>
public sealed class DeployWarning
{
    public string Message { get; init; } = string.Empty;
    public bool IsError { get; init; }
    public bool IsWarning { get; init; }
    public bool IsInfo => !IsError && !IsWarning;
}

/// <summary>GPO déployée par ADOps sur un poste, avec ses valeurs matérialisées.</summary>
public sealed class ClientGpoRecordViewModel
{
    public GpoDeploymentRecord Record { get; }
    public string GpoName => Record.GpoName;
    public string TemplateName { get; }
    public string DeployedText => Record.DeployedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string FilterGroupText => string.IsNullOrWhiteSpace(Record.FilterGroup)
        ? "Sans filtre — liée à l'OU"
        : Record.FilterGroup!;
    public bool CanDetach => !string.IsNullOrWhiteSpace(Record.FilterGroup);
    public IReadOnlyList<RegistryValueRow> Values { get; }
    public string SettingsCountText => Values.Count + (Values.Count > 1 ? " valeurs" : " valeur");

    public ClientGpoRecordViewModel(GpoDeploymentRecord record, string templateName)
    {
        Record = record;
        TemplateName = templateName;
        Values = ParseSettings(record.SettingsJson);
    }

    private static IReadOnlyList<RegistryValueRow> ParseSettings(string json)
    {
        var values = new List<RegistryValueRow>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return values;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return values;
            }

            foreach (var element in document.RootElement.EnumerateArray())
            {
                var key = element.TryGetProperty("key", out var keyElement) ? keyElement.GetString() : null;
                var name = element.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                var type = element.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
                var value = element.TryGetProperty("value", out var valueElement) ? valueElement.GetString() : null;
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                values.Add(new RegistryValueRow
                {
                    Key = key!,
                    Name = name!,
                    Type = FormatType(type),
                    Value = value ?? string.Empty,
                });
            }
        }
        catch (JsonException)
        {
            // Trace illisible : la GPO reste visible, sans détail de registre.
        }

        return values;
    }

    private static string FormatType(string? type) => type?.ToLowerInvariant() switch
    {
        "dword" => "REG_DWORD",
        "qword" => "REG_QWORD",
        "string" => "REG_SZ",
        "expandstring" => "REG_EXPAND_SZ",
        "multistring" => "REG_MULTI_SZ",
        "binary" => "REG_BINARY",
        _ => type ?? "REG_SZ",
    };
}

public sealed class RegistryValueRow
{
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

/// <summary>Valeurs regroupées par clé de registre pour la console du poste.</summary>
public sealed class RegistryGroupViewModel
{
    public string Key { get; init; } = string.Empty;
    public IReadOnlyList<RegistryValueRow> Values { get; init; } = Array.Empty<RegistryValueRow>();
    public string CountText => Values.Count + (Values.Count > 1 ? " valeurs" : " valeur");
}

/// <summary>Utilisateur AD DS sélectionnable pour le déploiement de GPO par portée Utilisateur.</summary>
public sealed partial class UserItemViewModel : ObservableObject
{
    private readonly Action<UserItemViewModel> _selectionChanged;

    public UserInfo Row { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private int _recordCount;

    public string Name => string.IsNullOrWhiteSpace(Row.DisplayName) ? Row.SamAccountName : Row.DisplayName!;
    public string Login => Row.SamAccountName;
    public string Department => string.IsNullOrWhiteSpace(Row.Department) ? "Sans département" : Row.Department!;
    public string OuText => string.IsNullOrWhiteSpace(Row.OuDistinguishedName) ? "—" : Row.OuDistinguishedName.Split(',').LastOrDefault()?.Trim() ?? "—";
    public string DistinguishedName => Row.DistinguishedName;
    public string Email => string.IsNullOrWhiteSpace(Row.Email) ? "—" : Row.Email!;
    public string Title => string.IsNullOrWhiteSpace(Row.Title) ? "—" : Row.Title!;
    public bool IsAccountEnabled => Row.Enabled;
    public bool IsAccountDisabled => !Row.Enabled;
    public bool IsAdministrator => Row.IsAdministrator;
    public bool IsAccountLocked => Row.AccountLocked;
    public string AccountStatusText
    {
        get
        {
            if (!Row.Enabled) return "Désactivé";
            if (Row.AccountLocked) return "Verrouillé";
            return IsAdministrator ? "Administrateur" : "Standard";
        }
    }
    public string AccountStatePillText => Row.Enabled ? "Activé" : "Désactivé";
    public string LastLogonText => Row.LastLogonUtc is null
        ? "Jamais connecté"
        : "Connexion : " + Row.LastLogonUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string RecordCountText => RecordCount switch
    {
        0 => "Aucune GPO ADOps",
        1 => RecordCount + " GPO",
        _ => RecordCount + " GPO",
    };

    public bool HasRecords => RecordCount > 0;

    public UserItemViewModel(UserInfo row, bool isSelected, Action<UserItemViewModel> selectionChanged)
    {
        Row = row;
        _isSelected = isSelected;
        _selectionChanged = selectionChanged;
    }

    partial void OnIsSelectedChanged(bool value) => _selectionChanged(this);

    partial void OnRecordCountChanged(int value)
    {
        OnPropertyChanged(nameof(RecordCountText));
        OnPropertyChanged(nameof(HasRecords));
    }
}

/// <summary>Département = regroupement d'après l'OU réelle des comptes utilisateurs.</summary>
public sealed partial class UserGroupViewModel : ObservableObject
{
    public string Name { get; }
    public string Subtitle { get; }
    public IReadOnlyList<UserItemViewModel> Users { get; }

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private string _selectionText = "0 / 0 sélectionné";

    public string Meta => Users.Count + (Users.Count > 1 ? " comptes" : " compte")
        + " · " + Users.Count(u => u.IsAccountEnabled) + " actif(s)"
        + " · " + Users.Count(u => u.IsAdministrator) + " administrateur(s) exclu(s) du ciblage";

    public bool IsAllSelected => Users.Count > 0 && Users.All(user => user.IsSelected);

    public UserGroupViewModel(string name, string subtitle, IReadOnlyList<UserItemViewModel> users)
    {
        Name = name;
        Subtitle = subtitle;
        Users = users;
        Recount();
    }

    public void Recount()
    {
        var selected = Users.Count(user => user.IsSelected);
        SelectionText = selected + " / " + Users.Count + " sélectionné" + (selected > 1 ? "s" : string.Empty);
        OnPropertyChanged(nameof(Meta));
        OnPropertyChanged(nameof(IsAllSelected));
    }

    private static string ShortOu(string dn)
    {
        var first = dn.Split(',', 2)[0];
        var index = first.IndexOf('=');
        return index >= 0 && index + 1 < first.Length ? first[(index + 1)..] : dn;
    }
}
