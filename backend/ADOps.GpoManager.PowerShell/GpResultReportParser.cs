using System.Globalization;
using System.Text;
using System.Xml;

namespace ADOps.GpoManager.PowerShell;

/// <summary>
/// Lecture des rapports gpresult : c'est ici, et uniquement ici, que la liste des
/// GPO réellement appliquées est dérivée de la sortie brute de l'outil.
///
/// <para>
/// <c>gpresult /r</c> ne préfixe jamais les noms par « GPO : » : la liste est un bloc
/// de lignes indentées sous la rubrique « Applied Group Policy Objects » (console
/// anglaise) ou « Stratégies de groupe appliquées » (console française), séparée du
/// titre par une ligne de tirets et suivie d'une ligne vide. Les GPO écartées par le
/// filtrage de sécurité figurent dans une rubrique distincte : elles ne doivent jamais
/// être présentées comme appliquées.
/// </para>
/// <para>
/// Le rapport XML (<c>gpresult /x</c>) est la source autoritaire quand il est disponible :
/// ses éléments <c>GPO</c> portent <c>FilterAllowed</c> et <c>AccessDenied</c>. Il utilise
/// plusieurs espaces de noms (<c>GroupPolicy/Rsop</c>, <c>GroupPolicy/Types</c>,
/// <c>GroupPolicy/Settings/*</c>) ; une XPath <c>//Gpo</c> sans gestion de namespace ne
/// sélectionne donc rien, d'où les tests sur <c>local-name()</c>.
/// </para>
/// </summary>
public static class GpResultReportParser
{
    private static readonly string[] AppliedSectionMarkers =
    [
        "applied group policy objects",
        "group policy objects applied",
        "strategies de groupe appliquees",
        "objets de strategie de groupe appliques",
    ];

    private static readonly string[] SectionStopMarkers =
    [
        "the following gpos were not applied",
        "was blocked by domain user",
        "part of the following security groups",
        "groupes de securite suivants",
        "pas ete appliques",
        "pas ete appliquees",
    ];

    private static readonly string[] PropertyLineMarkers =
    [
        "filtering:",
        "filtrage :",
        "links:",
        "liens :",
        "extensions:",
        "wmi filters:",
        "applink",
    ];

    /// <summary>Noms des GPO appliquées tels que listés par <c>gpresult /r</c> (texte, FR et EN).</summary>
    public static IReadOnlyList<string> ParseAppliedGpoNames(string? textReport)
    {
        var names = new List<string>();
        if (string.IsNullOrWhiteSpace(textReport))
        {
            return names;
        }

        var lines = textReport.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!IsAppliedSectionHeader(lines[i]))
            {
                continue;
            }

            var headerIndent = LeadingIndent(lines[i]);
            for (var j = i + 1; j < lines.Length; j++)
            {
                var entry = lines[j];
                if (entry.Trim().Length == 0)
                {
                    break;
                }

                if (IsUnderline(entry))
                {
                    continue;
                }

                if (LeadingIndent(entry) <= headerIndent || IsStopLine(entry))
                {
                    break;
                }

                var candidate = entry.Trim();
                if (IsPlaceholder(candidate) || IsPropertyLine(candidate))
                {
                    continue;
                }

                AddUnique(names, candidate);
            }
        }

        return names;
    }

    /// <summary>
    /// Noms des GPO appliquées lus dans un rapport <c>gpresult /x</c> : éléments <c>GPO</c>
    /// dont le filtrage de sécurité est accordé et l'accès autorisé.
    /// </summary>
    public static IReadOnlyList<string> ParseAppliedGpoNamesFromXml(string? xmlReport)
    {
        var names = new List<string>();
        if (string.IsNullOrWhiteSpace(xmlReport))
        {
            return names;
        }

        var document = new XmlDocument();
        try
        {
            document.LoadXml(xmlReport);
        }
        catch (XmlException)
        {
            return names;
        }

        var applied = document.SelectNodes("//*[local-name()='GPO'][*[local-name()='Name']]");
        if (applied is null)
        {
            return names;
        }

        foreach (XmlNode node in applied)
        {
            if (IsDenied(node))
            {
                continue;
            }

            var name = ReadChild(node, "Name");
            if (string.IsNullOrWhiteSpace(name) || IsPlaceholder(name))
            {
                continue;
            }

            AddUnique(names, name.Trim());
        }

        return names;
    }

    /// <summary>
    /// Isole le rapport XML dans une sortie qui peut être sérialisée en JSON par le
    /// hôte PowerShell ou polluée par du bruit de transport. Null si aucun rapport n'est présent.
    /// </summary>
    public static string? ExtractXmlReport(string? stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return null;
        }

        var text = stdout.Trim().TrimStart((char)0xFEFF);

        // Le corps d'une commande est sérialisé : un rapport renvoyé comme chaîne arrive
        // entre guillemets JSON, avec les chevrons échappés.
        if (text.StartsWith("\"", StringComparison.Ordinal))
        {
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(text);
                if (document.RootElement.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    text = document.RootElement.GetString() ?? string.Empty;
                }
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }

        var start = text.IndexOf("<?xml", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            start = text.IndexOf('<');
        }

        if (start < 0)
        {
            return null;
        }

        var end = text.LastIndexOf("</Rsop", StringComparison.OrdinalIgnoreCase);
        if (end >= start)
        {
            var closing = text.IndexOf('>', end);
            return closing > end ? text.Substring(start, closing - start + 1) : text[start..].Trim();
        }

        var last = text.LastIndexOf('>');
        return last > start ? text.Substring(start, last - start + 1) : null;
    }

    /// <summary>
    /// Liste des GPO appliquées dans la sortie d'une <c>GetGpResultCommand</c>.
    /// Le rapport texte fait foi ; s'il ne livre rien, le rapport XML obtenu en repli
    /// est analysé à son tour. Une liste vide reste un résultat valide : le poste peut
    /// légitimement n'avoir aucune stratégie appliquée.
    /// </summary>
    public static IReadOnlyList<string> FromCommandOutput(string? stdout)
    {
        var json = PowerShellOutputParser.ExtractJson(stdout);
        if (json.Length == 0 || json[0] != '{')
        {
            return Array.Empty<string>();
        }

        string rawOutput;
        string xmlReport;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return Array.Empty<string>();
            }

            rawOutput = ReadString(root, "RawOutput");
            xmlReport = ReadString(root, "XmlReport");
        }
        catch (System.Text.Json.JsonException)
        {
            return Array.Empty<string>();
        }

        var names = ParseAppliedGpoNames(rawOutput);
        return names.Count > 0
            ? names
            : ParseAppliedGpoNamesFromXml(ExtractXmlReport(xmlReport));
    }

    private static string ReadString(System.Text.Json.JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static bool IsAppliedSectionHeader(string line)
    {
        var normalized = Normalize(line);
        return normalized.Length > 0 && AppliedSectionMarkers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal));
    }

    private static bool IsStopLine(string line)
    {
        var normalized = Normalize(line);
        return SectionStopMarkers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal));
    }

    private static bool IsPropertyLine(string line)
    {
        var normalized = Normalize(line);
        return PropertyLineMarkers.Any(marker => normalized.StartsWith(marker, StringComparison.Ordinal));
    }

    private static bool IsPlaceholder(string value)
    {
        var normalized = Normalize(value);
        return normalized is "na" or "none" or "aucune" or "non applicable";
    }

    private static bool IsUnderline(string line)
    {
        var dashes = 0;
        foreach (var c in line)
        {
            if (c == '-')
            {
                dashes++;
            }
            else if (!char.IsWhiteSpace(c))
            {
                return false;
            }
        }

        return dashes >= 3;
    }

    private static int LeadingIndent(string line)
    {
        var columns = 0;
        foreach (var c in line)
        {
            if (c == ' ')
            {
                columns++;
            }
            else if (c == '\t')
            {
                columns += 4;
            }
            else
            {
                break;
            }
        }

        return columns;
    }

    private static bool IsDenied(XmlNode gpo)
    {
        var filterAllowed = ReadChild(gpo, "FilterAllowed");
        if (!string.IsNullOrWhiteSpace(filterAllowed)
            && bool.TryParse(filterAllowed.Trim(), out var allowed)
            && !allowed)
        {
            return true;
        }

        var accessDenied = ReadChild(gpo, "AccessDenied");
        return bool.TryParse(accessDenied?.Trim(), out var denied) && denied;
    }

    private static string? ReadChild(XmlNode parent, string localName)
    {
        var child = parent.SelectSingleNode("*[local-name()='" + localName + "']");
        return child?.InnerText;
    }

    private static void AddUnique(List<string> names, string name)
    {
        if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            names.Add(name);
        }
    }

    /// <summary>
    /// Forme comparable d'une ligne : minuscules, sans diacritiques ni ponctuation.
    /// Les libellés localisés de gpresult sont stables une fois les accents retirés,
    /// ce qui évite de dépendre de la page de codes ou de la langue du poste.
    /// </summary>
    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var stripped = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(stripped.Length);
        foreach (var c in stripped)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c) || c == ' ' || c == ':')
            {
                builder.Append(c);
            }
        }

        var result = builder.ToString().Normalize(NormalizationForm.FormC);
        while (result.Contains("  ", StringComparison.Ordinal))
        {
            result = result.Replace("  ", " ");
        }

        return result.Trim();
    }
}
