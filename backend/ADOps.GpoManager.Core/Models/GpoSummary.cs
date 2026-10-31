namespace ADOps.GpoManager.Core.Models;

/// <summary>État de configuration d'une GPO (propriété GpoStatus du module GroupPolicy).</summary>
public enum GpoStatus
{
    Unknown = 0,
    AllSettingsEnabled = 1,
    ComputerSettingsDisabled = 2,
    UserSettingsDisabled = 3,
    AllSettingsDisabled = 4,
}

/// <summary>Résumé d'un objet de stratégie de groupe (GPO), tel que retourné par Get-GPO.</summary>
public sealed class GpoSummary
{
    public Guid Id { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateTimeOffset? CreationTime { get; init; }
    public DateTimeOffset? ModificationTime { get; init; }
    public GpoStatus Status { get; init; }
    public string? DomainName { get; init; }

    /// <summary>Libellé du statut affiché dans la grille.</summary>
    public string StatusText => Status switch
    {
        GpoStatus.AllSettingsEnabled => "Tout activé",
        GpoStatus.ComputerSettingsDisabled => "Config. ordinateur désactivée",
        GpoStatus.UserSettingsDisabled => "Config. utilisateur désactivée",
        GpoStatus.AllSettingsDisabled => "Tout désactivé",
        _ => "Inconnu",
    };
}
