namespace ADOps.GpoManager.Core.Models;

public enum PolicyScope
{
    Computer = 0,
    User = 1,
}

public enum TemplateSupportStatus
{
    Available = 0,
    MissingPrerequisite = 1,
    NotSupported = 2,
    ComingSoon = 3,
}

public enum TemplateParameterType
{
    Text = 0,
    MultilineText = 1,
    Integer = 100,
    Boolean = 101,
    Enum = 102,
}

/// <summary>Paramètre à renseigner d'un modèle de stratégie.</summary>
public sealed class TemplateParameter
{
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public TemplateParameterType Type { get; init; }
    public string? Description { get; init; }
    public bool IsRequired { get; init; }
    public string? DefaultValue { get; init; }
    public IReadOnlyList<string>? EnumValues { get; init; }
    public string? Placeholder { get; init; }
}

/// <summary>
/// Modèle de stratégie du catalogue ADOps. Un modèle n'est pas une GPO du domaine :
/// il ne devient une GPO qu'après création explicite sur le domaine.
/// </summary>
public sealed class PolicyTemplate
{
    public int SchemaVersion { get; init; }
    public string TemplateId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ExpectedEffect { get; init; } = string.Empty;
    public PolicyScope Scope { get; init; }
    public string RiskLevel { get; init; } = "Low";
    public IReadOnlyList<string> SupportedClients { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Requirements { get; init; } = Array.Empty<string>();
    public IReadOnlyList<TemplateParameter> Parameters { get; init; } = Array.Empty<TemplateParameter>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public TemplateSupportStatus SupportStatus { get; init; }
    public string? SupportNote { get; init; }
}

/// <summary>Résultat de la validation des paramètres d'un modèle.</summary>
public sealed class TemplateValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public PolicyTemplate? Template { get; init; }
}
