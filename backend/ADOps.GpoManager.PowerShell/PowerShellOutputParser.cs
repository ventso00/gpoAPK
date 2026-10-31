using System.Globalization;
using System.Text.Json;
using ADOps.GpoManager.Core.Models;

namespace ADOps.GpoManager.PowerShell;

/// <summary>
/// Analyse la sortie JSON produite par les commandes PowerShell.
/// L'analyse est tolérante : une sortie polluée rend un résultat vide plutôt
/// qu'une exception.
/// </summary>
public sealed class PowerShellOutputParser
{
    /// <summary>
    /// Isole le JSON dans une sortie PowerShell qui peut être polluée.
    /// </summary>
    /// <remarks>
    /// La sortie est censée être du JSON compact, mais trois sources l'encrassent
    /// régulièrement et feraient échouer <c>JsonDocument.Parse</c> :
    /// les bannières de cmdlets natifs (<c>gpresult</c>, <c>Add-WindowsCapability</c>),
    /// les avertissements de <c>Invoke-Command</c> sérialisés en CLIXML
    /// (<c>&lt;Objs …&gt;</c>), et le bruit des runspaces internes.
    /// On localise donc le premier caractère de début et le dernier de fin
    /// d'une valeur JSON. Une sortie réellement vide reste vide : c'est un
    /// résultat valide, jamais une erreur.
    /// </remarks>
    public static string ExtractJson(string? stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return string.Empty;
        }

        var text = stdout.Trim();

        var start = text.IndexOfAny(new[] { '{', '[' });
        if (start < 0)
        {
            // Sortie scalaire (nombre, booléen, null ou chaîne JSON) : on valide
            // avec le parseur JSON plutôt que de rejeter aveuglément, afin que
            // ParseScalarString puisse traiter les valeurs non objet/tableau
            // (ex. un compteur « 3 » ou un nom d'utilisateur « "VENTSO\\x" »).
            try
            {
                using (JsonDocument.Parse(text)) { }
                return text;
            }
            catch (JsonException)
            {
                return string.Empty;
            }
        }

        var isArray = text[start] == '[';
        var end = text.LastIndexOf(isArray ? ']' : '}');
        if (end <= start)
        {
            return string.Empty;
        }

        return text.Substring(start, end - start + 1);
    }

    /// <summary>
    /// Analyse un document JSON complet. Retourne null si la sortie est vide
    /// ou illisible.
    /// </summary>
    public JsonElement? ParseRaw(string json)
    {
        if (!TryParseDocument(ExtractJson(json), out var document))
        {
            return null;
        }

        using (document)
        {
            return document.RootElement.Clone();
        }
    }

    /// <summary>
    /// Analyse la liste des GPO. Gère les trois formes produites par ConvertTo-Json :
    /// tableau (plusieurs GPO), objet unique (une seule GPO) et sortie vide (aucune GPO).
    /// Une liste vide est un résultat valide, pas une erreur (contrainte n°5).
    /// </summary>
    public IReadOnlyList<GpoSummary> ParseGpoList(string json)
    {
        if (!TryParseDocument(ExtractJson(json), out var document))
        {
            return Array.Empty<GpoSummary>();
        }

        using (document)
        {
            var root = document.RootElement;
            var list = new List<GpoSummary>();
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in root.EnumerateArray())
                {
                    list.Add(ParseGpo(element));
                }
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                list.Add(ParseGpo(root));
            }

            return list;
        }
    }

    /// <summary>Analyse la sortie de Get-Module -ListAvailable.</summary>
    public IReadOnlyList<PowerShellModule> ParseModules(string json)
    {
        if (!TryParseDocument(ExtractJson(json), out var document))
        {
            return Array.Empty<PowerShellModule>();
        }

        using (document)
        {
            var root = document.RootElement;
            var list = new List<PowerShellModule>();
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in root.EnumerateArray())
                {
                    list.Add(new PowerShellModule(
                        GetString(element, "Name") ?? string.Empty,
                        GetString(element, "Version") ?? string.Empty));
                }
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                list.Add(new PowerShellModule(
                    GetString(root, "Name") ?? string.Empty,
                    GetString(root, "Version") ?? string.Empty));
            }

            return list;
        }
    }

    /// <summary>
    /// Analyse une valeur scalaire en texte (ex. : $env:USERNAME retourné par
    /// Invoke-Command). Accepte également un JSON non quoted (nombre, booléen),
    /// que PowerShell produit pour certaines valeurs.
    /// </summary>
    public string? ParseScalarString(string json)
    {
        if (!TryParseDocument(ExtractJson(json), out var document))
        {
            return null;
        }

        using (document)
        {
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.String => document.RootElement.GetString(),
                JsonValueKind.Number => document.RootElement.GetRawText(),
                JsonValueKind.True => "True",
                JsonValueKind.False => "False",
                _ => null,
            };
        }
    }

    /// <summary>
    /// Analyse un JSON dont la racine est un objet unique, sans Lever d'exception.
    /// </summary>
    public static bool TryParseObject(string json, out JsonElement element)
    {
        element = default;
        if (!TryParseDocument(ExtractJson(json), out var document))
        {
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            element = document.RootElement.Clone();
            return true;
        }
    }

    /// <summary>
    /// Analyse un JSON dont la racine est un tableau, et énère ses éléments.
    /// Utilisé par les sorties multi-lignes (balayage réseau, rapports).
    /// </summary>
    public static IEnumerable<JsonElement> EnumerateArray(string json)
    {
        if (!TryParseDocument(ExtractJson(json), out var document))
        {
            yield break;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in root.EnumerateArray())
                {
                    yield return element.Clone();
                }
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                // PowerShell sérialise un élément unique sans tableau.
                yield return root.Clone();
            }
        }
    }

    private static bool TryParseDocument(string candidate, out JsonDocument document)
    {
        document = null!;
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        try
        {
            document = JsonDocument.Parse(candidate);
            return true;
        }
        catch (JsonException)
        {
            // Sortie illisible : aucun élément exploitable, mais ce n'est pas
            // une exception qui remonte jusqu'à l'écran.
            return false;
        }
    }

    private static GpoSummary ParseGpo(JsonElement element) => new()
    {
        Id = ParseGuid(element, "Id"),
        DisplayName = GetString(element, "DisplayName") ?? "Sans nom",
        Description = GetString(element, "Description"),
        CreationTime = ParseDateTimeOffset(element, "CreationTime"),
        ModificationTime = ParseDateTimeOffset(element, "ModificationTime"),
        Status = ParseGpoStatus(GetString(element, "GpoStatus")),
        DomainName = GetString(element, "DomainName"),
    };

    private static Guid ParseGuid(JsonElement element, string propertyName)
    {
        var value = GetString(element, propertyName);
        return Guid.TryParse(value, out var guid) ? guid : Guid.Empty;
    }

    private static DateTimeOffset? ParseDateTimeOffset(JsonElement element, string propertyName)
    {
        var value = GetString(element, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result))
        {
            return result;
        }

        return null;
    }

    private static GpoStatus ParseGpoStatus(string? value) => value switch
    {
        "AllSettingsEnabled" => GpoStatus.AllSettingsEnabled,
        "ComputerSettingsDisabled" => GpoStatus.ComputerSettingsDisabled,
        "UserSettingsDisabled" => GpoStatus.UserSettingsDisabled,
        "AllSettingsDisabled" => GpoStatus.AllSettingsDisabled,
        _ => GpoStatus.Unknown,
    };

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

/// <summary>Module PowerShell retourné par Get-Module -ListAvailable.</summary>
public sealed record PowerShellModule(string Name, string Version);