using ADOps.GpoManager.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ADOps.GpoManager.App.ViewModels;

/// <summary>Compte client proposé comme cible de déploiement.</summary>
public sealed partial class TargetAccountRow : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string? _note;

    public string DistinguishedName { get; init; } = string.Empty;

    public string SamAccountName { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string? Department { get; init; }

    public string? OuName { get; init; }

    public string? OuDistinguishedName { get; init; }

    public bool IsAdministrator { get; init; }

    public bool Enabled { get; init; }

    public bool AccountLocked { get; init; }

    public string StatusText => !Enabled ? "Désactivé" : AccountLocked ? "Verrouillé" : IsAdministrator ? "Administrateur" : "Standard";

    public UserInfo ToUserInfo() => new()
    {
        SamAccountName = SamAccountName,
        DistinguishedName = DistinguishedName,
        DisplayName = DisplayName,
        Department = Department,
        OuDistinguishedName = OuDistinguishedName,
        IsAdministrator = IsAdministrator,
        Enabled = Enabled,
        AccountLocked = AccountLocked,
    };
}

/// <summary>Poste client proposé comme cible de déploiement.</summary>
public sealed partial class TargetComputerRow : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string? _note;

    public string DistinguishedName { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? OuName { get; init; }

    public string? OuDistinguishedName { get; init; }

    public string? OperatingSystem { get; init; }

    public bool Enabled { get; init; }

    public string StatusText => Enabled ? "Actif" : "Désactivé";

    public ComputerInfo ToComputerInfo() => new()
    {
        Name = Name,
        DistinguishedName = DistinguishedName,
        SamAccountName = Name + "$",
        OuDistinguishedName = OuDistinguishedName ?? string.Empty,
        OperatingSystem = OperatingSystem,
        Enabled = Enabled,
    };
}

/// <summary>GPO sélectionnable (présente dans le domaine ou issue du catalogue).</summary>
public sealed partial class GpoChoice : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Vrai si la GPO existe déjà dans le domaine.</summary>
    public bool ExistsInDomain { get; init; }

    /// <summary>Identifiant de la GPO (Guid) — vide pour un modèle du catalogue.</summary>
    public Guid Id { get; init; }

    /// <summary>Identifiant du modèle du catalogue (vide pour une GPO du domaine).</summary>
    public string? TemplateId { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string Category { get; init; } = "Domaine";

    public string RiskLevel { get; init; } = "Low";

    public string? StatusText { get; init; }

    public string ScopeText { get; init; } = "—";

    public string OriginText => ExistsInDomain ? "GPO du domaine" : "Modèle du catalogue";

    public string RiskText => RiskLevel switch
    {
        "High" => "Risque élevé",
        "Medium" => "Risque modéré",
        _ => "Risque faible",
    };

    public GpoSummary ToSummary() => new()
    {
        Id = Id,
        DisplayName = DisplayName,
        Description = Description,
    };
}
