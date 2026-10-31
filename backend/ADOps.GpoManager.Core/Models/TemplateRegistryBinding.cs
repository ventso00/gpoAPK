namespace ADOps.GpoManager.Core.Models;

/// <summary>
/// Liaison exécutable d'un modèle du catalogue : valeurs par défaut des
/// paramètres et écritures de registre réellement injectées dans la GPO.
/// Le JSON du modèle reste descriptif ; cette liaison porte la sémantique
/// technique (chemins, types, conditions) qui rend le modèle déployable.
/// </summary>
public sealed class TemplateRegistryBinding
{
    public string TemplateId { get; init; } = string.Empty;

    /// <summary>Catégorie d'affichage (alignée sur le mode démonstration).</summary>
    public string? Category { get; init; }

    /// <summary>Valeurs par défaut, indexées par nom de paramètre.</summary>
    public IReadOnlyDictionary<string, string> DefaultParameters { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Écritures de registre, dans l'ordre de déclaration.</summary>
    public IReadOnlyList<TemplateRegistryEntry> Entries { get; init; }
        = Array.Empty<TemplateRegistryEntry>();
}

/// <summary>
/// Une écriture de registre d'un modèle. Les jetons <c>${parametre}</c> sont
/// remplacés par la valeur saisie ; <c>${item}</c> et <c>${index}</c> ne sont
/// disponibles que pour une entrée développée en liste.
/// </summary>
public sealed class TemplateRegistryEntry
{
    /// <summary>Clé complète avec ruche (HKLM\... ou HKCU\...).</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Nom de valeur ; peut contenir ${parametre}, ${item}, ${index}.</summary>
    public string ValueName { get; init; } = string.Empty;

    /// <summary>Type ADSI : String, ExpandString, Binary, DWord, QWord, MultiString.</summary>
    public string ValueType { get; init; } = "String";

    /// <summary>Donnée ; peut contenir ${parametre}, ${item}, ${index}.</summary>
    public string Data { get; init; } = string.Empty;

    /// <summary>Condition : nom du paramètre à évaluer.</summary>
    public string? WhenParameter { get; init; }

    /// <summary>Condition : inclure si la valeur du paramètre est égale (insensible à la casse).</summary>
    public string? WhenEquals { get; init; }

    /// <summary>Condition : inclure si la valeur du paramètre diffère.</summary>
    public string? WhenNotEquals { get; init; }

    /// <summary>Condition : inclure si le paramètre booléen vaut vrai/faux.</summary>
    public bool? WhenTrue { get; init; }

    /// <summary>
    /// Nom du paramètre multiligne à développer : une écriture par ligne non vide,
    /// avec ${item} (la ligne) et ${index} (1, 2, 3…).
    /// </summary>
    public string? ForEachParameter { get; init; }
}
