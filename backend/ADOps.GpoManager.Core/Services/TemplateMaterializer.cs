using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ADOps.GpoManager.Core.Commands;
using ADOps.GpoManager.Core.Models;

namespace ADOps.GpoManager.Core.Services;

/// <summary>
/// Transforme un modèle du catalogue en écritures de registre concrètes.
/// Les valeurs par défaut sont appliquées, les conditions évaluées et les
/// listes multilignes développées (URLBlocklist, règles ASR…). Le résultat
/// est directement injectable dans une GPO par Set-GPRegistryValue.
/// </summary>
public static class TemplateMaterializer
{
    private static readonly Regex TokenPattern = new(@"\$\{(?<name>[A-Za-z0-9_.-]+)\}", RegexOptions.Compiled);

    private static readonly HashSet<string> RegistryRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        "HKLM", "HKCU",
    };

    /// <summary>
    /// Valeurs effectives : valeurs saisies, complétées par les valeurs par défaut.
    /// </summary>
    public static Dictionary<string, string> MergeDefaults(
        TemplateRegistryBinding binding,
        IReadOnlyDictionary<string, object?>? parameters)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in binding.DefaultParameters)
        {
            values[pair.Key] = pair.Value ?? string.Empty;
        }

        if (parameters is not null)
        {
            foreach (var pair in parameters)
            {
                values[pair.Key] = NormalizeValue(pair.Value);
            }
        }

        return values;
    }

    /// <summary>Matérialise les écritures de registre du modèle.</summary>
    public static IReadOnlyList<GpoRegistrySetting> Materialize(
        TemplateRegistryBinding binding,
        IReadOnlyDictionary<string, object?>? parameters)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var values = MergeDefaults(binding, parameters);
        var settings = new List<GpoRegistrySetting>();

        foreach (var entry in binding.Entries)
        {
            if (!ConditionMatches(entry, values))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(entry.ForEachParameter))
            {
                var source = GetValue(values, entry.ForEachParameter);
                var lines = source
                    .Split('\n')
                    .Select(line => line.Trim().Trim('\r'))
                    .Where(line => line.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                for (var index = 0; index < lines.Count; index++)
                {
                    var tokens = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase)
                    {
                        ["item"] = lines[index],
                        ["index"] = (index + 1).ToString(CultureInfo.InvariantCulture),
                    };

                    settings.Add(BuildSetting(entry, tokens));
                }

                continue;
            }

            settings.Add(BuildSetting(entry, values));
        }

        if (settings.Count == 0)
        {
            throw new ArgumentException(
                "Aucune valeur de registre ne résulte des paramètres fournis : la GPO serait vide. "
                + "Vérifiez les paramètres du modèle « " + binding.TemplateId + " ».");
        }

        return settings;
    }

    private static GpoRegistrySetting BuildSetting(
        TemplateRegistryEntry entry,
        IReadOnlyDictionary<string, string> tokens)
    {
        var key = Substitute(entry.Key, tokens);
        var valueName = Substitute(entry.ValueName, tokens);
        var data = Substitute(entry.Data, tokens);

        var setting = new GpoRegistrySetting
        {
            Key = key,
            ValueName = valueName,
            ValueType = entry.ValueType,
            Value = data,
        };

        setting.Validate();
        return setting;
    }

    private static bool ConditionMatches(
        TemplateRegistryEntry entry,
        IReadOnlyDictionary<string, string> values)
    {
        if (string.IsNullOrWhiteSpace(entry.WhenParameter))
        {
            return true;
        }

        var actual = GetValue(values, entry.WhenParameter);

        if (entry.WhenTrue.HasValue)
        {
            return ParseBoolean(actual) == entry.WhenTrue.Value;
        }

        if (entry.WhenEquals is not null)
        {
            return string.Equals(actual.Trim(), entry.WhenEquals.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        if (entry.WhenNotEquals is not null)
        {
            return !string.Equals(actual.Trim(), entry.WhenNotEquals.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private static string GetValue(IReadOnlyDictionary<string, string> values, string name)
        => values.TryGetValue(name, out var value) ? value : string.Empty;

    private static string Substitute(string template, IReadOnlyDictionary<string, string> values)
        => TokenPattern.Replace(template, match =>
        {
            var name = match.Groups["name"].Value;
            return values.TryGetValue(name, out var value) ? value : string.Empty;
        });

    private static string NormalizeValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            bool boolean => boolean ? "true" : "false",
            string text => text,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }

    internal static bool ParseBoolean(string value)
        => value.Trim().ToLowerInvariant() is "true" or "1" or "yes" or "oui" or "vrai" or "activé" or "active";

    /// <summary>Contrôles communs aux entrées et aux paramètres du catalogue.</summary>
    internal static void ValidateRegistryIdentity(string key, string valueName, string valueType)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("La clé de registre est requise.");
        }

        var separator = key.IndexOf('\\');
        if (separator <= 0 || !RegistryRoots.Contains(key[..separator]))
        {
            throw new ArgumentException("La clé doit commencer par HKLM\\ ou HKCU\\ : « " + key + " ».");
        }

        if (key.Length > 260)
        {
            throw new ArgumentException("La clé de registre dépasse 260 caractères.");
        }

        if (key.Any(char.IsControl))
        {
            throw new ArgumentException("La clé de registre contient un caractère de contrôle.");
        }

        if (string.IsNullOrWhiteSpace(valueName))
        {
            throw new ArgumentException("Le nom de la valeur de registre est requis pour « " + key + " ».");
        }

        if (valueName.Length > 255 || valueName.Any(char.IsControl))
        {
            throw new ArgumentException("Nom de valeur invalide pour « " + key + " ».");
        }

        var allowed = new[] { "String", "ExpandString", "Binary", "DWord", "QWord", "MultiString" };
        if (!allowed.Contains(valueType, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Type de valeur non pris en charge : " + valueType + ".");
        }
    }

    /// <summary>Construit le commentaire de GPO à partir du modèle et des paramètres.</summary>
    public static string BuildComment(
        PolicyTemplate template,
        IReadOnlyDictionary<string, string> values)
    {
        var builder = new StringBuilder();
        builder.Append("ADOps — ").Append(template.DisplayName);
        builder.Append(" · modèle ").Append(template.TemplateId);
        var selected = template.Parameters
            .Where(parameter => values.ContainsKey(parameter.Name))
            .Take(6)
            .Select(parameter => parameter.Name + "=" + values[parameter.Name]);
        var suffix = string.Join(", ", selected);
        if (suffix.Length > 0)
        {
            builder.Append(" [").Append(suffix).Append(']');
        }

        return builder.ToString();
    }
}
