using ADOps.GpoManager.Core.Commands;
using ADOps.GpoManager.Core.Models;

namespace ADOps.GpoManager.Core.Services;

/// <summary>Catalogue de modèles de stratégies (distinct des GPO réellement présentes dans le domaine).</summary>
public interface IGpoCatalogService
{
    IReadOnlyList<PolicyTemplate> GetTemplates();
    PolicyTemplate? GetById(string templateId);
    TemplateValidationResult ValidateParameters(string templateId, IReadOnlyDictionary<string, object?> parameters);

    /// <summary>
    /// Liaison exécutable du modèle (valeurs par défaut + écritures de registre).
    /// Null si le modèle n'a pas de liaison : il reste consultable mais n'est
    /// pas déployable.
    /// </summary>
    TemplateRegistryBinding? GetBinding(string templateId);

    /// <summary>
    /// Matérialise le modèle en écritures de registre. Les paramètres manquants
    /// sont complétés par les valeurs par défaut, puis validés. Lève
    /// <see cref="ArgumentException"/> si les paramètres ou la liaison sont invalides.
    /// </summary>
    IReadOnlyList<GpoRegistrySetting> Materialize(
        string templateId,
        IReadOnlyDictionary<string, object?> parameters);
}
