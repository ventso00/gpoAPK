using System.Globalization;
using System.Text.Json;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.PowerShell;

namespace ADOps.GpoManager.ActiveDirectory;

/// <summary>
/// Analyse les sorties JSON des commandes d'inventaire AD DS.
/// Gère les trois formes de ConvertTo-Json (5.1) : tableau, objet unique, sortie vide.
/// L'analyse est tolérante : une sortie polluée ou illisible produit un résultat vide
/// plutôt qu'une exception.
/// </summary>
public sealed class AdOutputParser
{
    /// <summary>Analyse la liste des OU. Une sortie vide retourne une liste vide (pas une erreur).</summary>
    public IReadOnlyList<OuInfo> ParseOus(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<OuInfo>();
        }

        var list = new List<OuInfo>();
        foreach (var element in EnumerateElements(json))
        {
            var distinguishedName = GetString(element, "DistinguishedName");
            if (string.IsNullOrWhiteSpace(distinguishedName))
            {
                continue;
            }

            list.Add(new OuInfo
            {
                Name = GetString(element, "Name") ?? string.Empty,
                DistinguishedName = distinguishedName,
                CanonicalName = GetString(element, "CanonicalName"),
                ParentDistinguishedName = DeriveParentDistinguishedName(distinguishedName),
            });
        }

        return list;
    }

    /// <summary>Analyse la liste des ordinateurs. Les attributs manquants (OS, dernière ouverture de session) sont tolérés.</summary>
    public IReadOnlyList<ComputerInfo> ParseComputers(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<ComputerInfo>();
        }

        var list = new List<ComputerInfo>();
        foreach (var element in EnumerateElements(json))
        {
            var distinguishedName = GetString(element, "DistinguishedName");
            if (string.IsNullOrWhiteSpace(distinguishedName))
            {
                continue;
            }

            list.Add(new ComputerInfo
            {
                Name = GetString(element, "Name") ?? string.Empty,
                DistinguishedName = distinguishedName,
                SamAccountName = GetString(element, "SamAccountName"),
                OuDistinguishedName = DeriveParentDistinguishedName(distinguishedName),
                OperatingSystem = GetString(element, "OperatingSystem"),
                OperatingSystemVersion = GetString(element, "OperatingSystemVersion"),
                Enabled = GetBoolean(element, "Enabled", defaultValue: true),
                LastLogonUtc = ParseDateTimeOffset(GetString(element, "LastLogonUtc")),
                IpAddress = GetString(element, "IPv4Address"),
            });
        }

        return list;
    }

    /// <summary>
    /// Analyse la sortie de ListUsersCommand (Get-ADUser).
    /// </summary>
    /// <param name="json">Sortie JSON de la commande.</param>
    /// <param name="administratorDns">
    /// DistinguishedNames des comptes membres d'un groupe d'administration.
    /// Un compte absent de cet ensemble est classé « standard » : la
    /// classification est un fait observé, jamais une supposition.
    /// </param>
    public IReadOnlyList<UserInfo> ParseUsers(string json, IReadOnlySet<string>? administratorDns = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<UserInfo>();
        }

        var admins = administratorDns ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<UserInfo>();
        foreach (var element in EnumerateElements(json))
        {
            var distinguishedName = GetString(element, "DistinguishedName");
            if (string.IsNullOrWhiteSpace(distinguishedName))
            {
                continue;
            }

            list.Add(new UserInfo
            {
                SamAccountName = GetString(element, "SamAccountName") ?? string.Empty,
                DistinguishedName = distinguishedName,
                DisplayName = GetString(element, "DisplayName"),
                GivenName = GetString(element, "GivenName"),
                Surname = GetString(element, "Surname"),
                Email = GetString(element, "EmailAddress"),
                Title = GetString(element, "Title"),
                Department = GetString(element, "Department"),
                Enabled = GetBoolean(element, "Enabled", defaultValue: true),
                IsAdministrator = admins.Contains(distinguishedName),
                AccountLocked = GetBoolean(element, "AccountLocked", defaultValue: false),
                PasswordNeverExpires = GetBoolean(element, "PasswordNeverExpires", defaultValue: false),
                LastLogonUtc = ParseDateTimeOffset(GetString(element, "LastLogonUtc")),
                OuDistinguishedName = DeriveParentDistinguishedName(distinguishedName),
            });
        }

        return list;
    }

    /// <summary>
    /// Analyse la sortie de ListDomainAdministratorsCommand : une liste de
    /// DistinguishedNames. Tolère les trois formes possibles de
    /// <c>ConvertTo-Json</c> : tableau, objet unique (un seul membre) et
    /// scalaire (chaîne).
    /// </summary>
    public IReadOnlySet<string> ParseAdministratorDns(string json)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.String)
            {
                AddIfPresent(result, root.GetString());
                return result;
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                AddIfPresent(result, GetString(root, "DistinguishedName"));
                return result;
            }

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in root.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.String)
                    {
                        AddIfPresent(result, element.GetString());
                        continue;
                    }

                    if (element.ValueKind == JsonValueKind.Object)
                    {
                        AddIfPresent(result, GetString(element, "DistinguishedName"));
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Sortie illisible : aucune classification n'est appliquée.
        }

        return result;
    }

    private static void AddIfPresent(HashSet<string> target, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target.Add(value.Trim());
        }
    }

    /// <summary>Analyse la sortie de ListGpoLinksCommand : { DomainLinks : [...], OuLinks : [...] }.</summary>
    public IReadOnlyList<GpoLinkInfo> ParseLinks(string json)
    {
        if (!PowerShellOutputParser.TryParseObject(json, out var root))
        {
            return Array.Empty<GpoLinkInfo>();
        }

        var links = new List<GpoLinkInfo>();

        if (root.TryGetProperty("DomainLinks", out var domainLinks))
        {
            foreach (var element in EnumerateElements(domainLinks))
            {
                links.Add(ParseLink(element, GpoLinkTargetType.Domain));
            }
        }

        if (root.TryGetProperty("OuLinks", out var ouLinks))
        {
            foreach (var element in EnumerateElements(ouLinks))
            {
                links.Add(ParseLink(element, GpoLinkTargetType.OrganizationalUnit));
            }
        }

        return links;
    }

    private static IEnumerable<JsonElement> EnumerateElements(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                yield return item;
            }
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            // ConvertTo-Json 5.1 sérialise un élément unique comme objet, non tableau.
            yield return element;
        }
    }

    /// <summary>
    /// Énumère les éléments d'une sortie JSON. Délègue à
    /// <see cref="PowerShellOutputParser"/> pour l'isolement du JSON et
    /// l'absorption d'une sortie polluée (CLIXML, bannières de cmdlets).
    /// </summary>
    private static IEnumerable<JsonElement> EnumerateElements(string json)
        => PowerShellOutputParser.EnumerateArray(json);

    private static GpoLinkInfo ParseLink(JsonElement element, GpoLinkTargetType fallbackType)
    {
        var typeValue = GetString(element, "TargetType");
        var targetType = string.Equals(typeValue, "Domain", StringComparison.OrdinalIgnoreCase)
            ? GpoLinkTargetType.Domain
            : string.Equals(typeValue, "OU", StringComparison.OrdinalIgnoreCase)
                ? GpoLinkTargetType.OrganizationalUnit
                : fallbackType;

        return new GpoLinkInfo
        {
            GpoId = ParseGuid(GetString(element, "GpoId")),
            TargetDistinguishedName = GetString(element, "Target") ?? string.Empty,
            TargetType = targetType,
            Order = GetInt32(element, "Order"),
            IsEnabled = GetBoolean(element, "Enabled", defaultValue: true),
        };
    }

    /// <summary>Le parent d'un DN est le DN sans son premier composant RDN (CN=...,OU=...).</summary>
    internal static string? DeriveParentDistinguishedName(string distinguishedName)
    {
        var commaIndex = distinguishedName.IndexOf(',');
        return commaIndex > 0 && commaIndex < distinguishedName.Length - 1
            ? distinguishedName[(commaIndex + 1)..]
            : null;
    }

    private static Guid ParseGuid(string? value)
        => Guid.TryParse(value, out var guid) ? guid : Guid.Empty;

    private static int GetInt32(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;

    private static bool GetBoolean(JsonElement element, string propertyName, bool defaultValue)
    {
        if (element.TryGetProperty(propertyName, out var value))
        {
            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => defaultValue,
            };
        }

        return defaultValue;
    }

    private static string? GetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? ParseDateTimeOffset(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result)
            ? result
            : null;
}
