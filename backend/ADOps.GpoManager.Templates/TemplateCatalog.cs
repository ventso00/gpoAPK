using System.Globalization;
using System.Text.Json;
using ADOps.GpoManager.Core.Commands;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Services;

namespace ADOps.GpoManager.Templates;

/// <summary>
/// Catalogue de modèles de stratégies ADOps. Les modèles sont des fichiers JSON
/// versionnés, embarqués dans l'assembly. Un modèle n'est PAS une GPO du domaine :
/// il ne devient une GPO qu'après création explicite sur le domaine.
/// </summary>
public sealed class TemplateCatalog : IGpoCatalogService
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly Dictionary<string, PolicyTemplate> _templates;

    public TemplateCatalog()
    {
        _templates = LoadEmbeddedTemplates();
    }

    public IReadOnlyList<PolicyTemplate> GetTemplates()
        => _templates.Values.OrderBy(template => template.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(template => template.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public PolicyTemplate? GetById(string templateId)
        => !string.IsNullOrWhiteSpace(templateId) && _templates.TryGetValue(templateId.Trim(), out var template)
            ? template
            : null;

    public TemplateRegistryBinding? GetBinding(string templateId)
        => !string.IsNullOrWhiteSpace(templateId)
            && TemplateRegistryBindings.All.TryGetValue(templateId.Trim(), out var binding)
                ? binding
                : null;

    public IReadOnlyList<GpoRegistrySetting> Materialize(
        string templateId,
        IReadOnlyDictionary<string, object?> parameters)
    {
        var binding = GetBinding(templateId)
            ?? throw new ArgumentException("Le modèle « " + templateId + " » n'a pas de liaison de registre : il n'est pas déployable.");

        var validation = ValidateParameters(templateId, parameters);
        if (!validation.IsValid)
        {
            throw new ArgumentException(string.Join(" ", validation.Errors));
        }

        return TemplateMaterializer.Materialize(binding, parameters);
    }

    public TemplateValidationResult ValidateParameters(string templateId, IReadOnlyDictionary<string, object?> parameters)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        var template = GetById(templateId);
        if (template is null)
        {
            errors.Add("Modèle introuvable : « " + templateId + " ».");
            return new TemplateValidationResult { Errors = errors };
        }

        // Les valeurs par défaut de la liaison exécutable complètent la saisie :
        // un paramètre requis mais doté d'un défaut n'est jamais bloquant.
        var effective = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in parameters ?? new Dictionary<string, object?>())
        {
            effective[pair.Key] = pair.Value;
        }

        var binding = GetBinding(templateId);
        if (binding is not null)
        {
            foreach (var pair in binding.DefaultParameters)
            {
                if (!effective.TryGetValue(pair.Key, out var current) || current is null || string.IsNullOrWhiteSpace(current.ToString()))
                {
                    effective[pair.Key] = pair.Value;
                }
            }
        }

        if (template.SupportStatus != TemplateSupportStatus.Available)
        {
            errors.Add("Le modèle « " + template.DisplayName + " » n'est pas disponible : "
                + (template.SupportNote ?? template.SupportStatus.ToString())
                + ". Aucun paramètre n'est validé tant que le modèle n'est pas disponible.");
            return new TemplateValidationResult { Errors = errors };
        }

        var knownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in template.Parameters)
        {
            knownNames.Add(parameter.Name);

            if (parameter.IsRequired && (!effective.TryGetValue(parameter.Name, out var value) || value is null || string.IsNullOrWhiteSpace(value.ToString())))
            {
                errors.Add("Paramètre requis manquant : « " + parameter.DisplayName + " » (" + parameter.Name + ").");
                continue;
            }

            if (!effective.TryGetValue(parameter.Name, out var raw) || raw is null)
            {
                continue;
            }

            var parameterErrors = ValidateValue(parameter, raw);
            errors.AddRange(parameterErrors);
        }

        foreach (var key in effective.Keys)
        {
            if (!knownNames.Contains(key))
            {
                warnings.Add("Paramètre inconnu ignoré : « " + key + " ».");
            }
        }

        return new TemplateValidationResult
        {
            Errors = errors,
            Warnings = warnings,
            IsValid = errors.Count == 0,
            Template = template,
        };
    }

    private static IEnumerable<string> ValidateValue(TemplateParameter parameter, object? raw)
    {
        var errors = new List<string>();

        switch (parameter.Type)
        {
            case TemplateParameterType.Text:
            case TemplateParameterType.MultilineText:
                if (raw is not string)
                {
                    errors.Add("Le paramètre « " + parameter.DisplayName + " » doit être du texte.");
                }

                break;

            case TemplateParameterType.Integer:
                if (raw is not long && raw is not int && !(raw is string s && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
                {
                    errors.Add("Le paramètre « " + parameter.DisplayName + " » doit être un nombre entier.");
                }

                break;

            case TemplateParameterType.Boolean:
                if (raw is not bool && !(raw is string bs && bool.TryParse(bs, out _)))
                {
                    errors.Add("Le paramètre « " + parameter.DisplayName + " » doit être vrai ou faux.");
                }

                break;

            case TemplateParameterType.Enum:
                var allowed = parameter.EnumValues ?? Array.Empty<string>();
                var value = raw.ToString() ?? string.Empty;
                if (allowed.Count == 0 || !allowed.Contains(value, StringComparer.OrdinalIgnoreCase))
                {
                    errors.Add("La valeur « " + value + " » n'est pas autorisée pour « " + parameter.DisplayName
                        + " ». Valeurs possibles : " + (allowed.Count == 0 ? "(aucune)" : string.Join(", ", allowed)) + ".");
                }

                break;

            default:
                errors.Add("Type de paramètre non pris en charge pour « " + parameter.DisplayName + " ».");
                break;
        }

        return errors;
    }

    private static Dictionary<string, PolicyTemplate> LoadEmbeddedTemplates()
    {
        var assembly = typeof(TemplateCatalog).Assembly;
        var templates = new Dictionary<string, PolicyTemplate>(StringComparer.OrdinalIgnoreCase);

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(stream);
                var root = document.RootElement;

                var schemaVersion = root.TryGetProperty("schemaVersion", out var versionElement) && versionElement.ValueKind == JsonValueKind.Number
                    ? versionElement.GetInt32()
                    : 0;

                if (schemaVersion != CurrentSchemaVersion)
                {
                    // Un modèle dont le schéma n'est pas connu est refusé, jamais deviné.
                    continue;
                }

                var template = root.Deserialize<PolicyTemplate>(SerializerOptions);
                if (template is not null && !string.IsNullOrWhiteSpace(template.TemplateId))
                {
                    templates[template.TemplateId] = template;
                }
            }
            catch (JsonException)
            {
                // Un modèle JSON invalide est ignoré : le catalogue reste disponible.
            }
        }

        return templates;
    }
}
