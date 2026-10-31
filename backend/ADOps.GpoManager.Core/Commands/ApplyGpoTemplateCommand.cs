using System.Globalization;
using System.Text;

namespace ADOps.GpoManager.Core.Commands;

/// <summary>
/// Crée ou met à jour une GPO portant les écritures de registre d'un modèle
/// du catalogue. Les valeurs déjà écrites par ADOps sont remplacées ; les
/// listes indexées (URLBlocklist, règles ASR) sont nettoyées avant réécriture
/// pour qu'une liste raccourcie ne laisse pas d'anciennes entrées.
/// </summary>
public sealed class ApplyGpoTemplateCommand : PowerShellCommand
{
    /// <summary>Nombre maximal d'index nettoyés pour une liste développée.</summary>
    private const int MaxIndexedValues = 512;

    public string GpoName { get; }

    public string? Comment { get; }

    public IReadOnlyList<GpoRegistrySetting> Settings { get; }

    public override string Name => "ApplyGpoTemplate";
    public override TimeSpan Timeout { get; } = TimeSpan.FromMinutes(5);

    public ApplyGpoTemplateCommand(
        string gpoName,
        IReadOnlyList<GpoRegistrySetting> settings,
        string? comment = null)
    {
        if (string.IsNullOrWhiteSpace(gpoName))
        {
            throw new ArgumentException("Le nom de la GPO est requis.", nameof(gpoName));
        }

        if (gpoName.Length > 120)
        {
            throw new ArgumentException("Le nom de la GPO ne peut pas dépasser 120 caractères.", nameof(gpoName));
        }

        if (gpoName.Any(char.IsControl))
        {
            throw new ArgumentException("Le nom de la GPO contient un caractère de contrôle.", nameof(gpoName));
        }

        GpoName = gpoName.Trim();
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();

        Settings = settings ?? Array.Empty<GpoRegistrySetting>();
        if (Settings.Count == 0)
        {
            throw new ArgumentException("Une GPO de modèle doit contenir au moins une écriture de registre.", nameof(settings));
        }

        foreach (var setting in Settings)
        {
            setting.Validate();
        }
    }

    public override string ScriptBody
    {
        get
        {
            var name = Escape(GpoName);
            var comment = Comment is null ? string.Empty : " -Comment '" + Escape(Comment) + "'";
            var script = new StringBuilder();

            script.AppendLine("Import-Module GroupPolicy -ErrorAction Stop");
            script.AppendLine("$gpo = Get-GPO -Name '" + name + "' -ErrorAction SilentlyContinue");
            script.AppendLine("$created = $false");
            script.AppendLine("if ($null -eq $gpo) {");
            script.AppendLine("  $gpo = New-GPO -Name '" + name + "'" + comment + " -ErrorAction Stop");
            script.AppendLine("  $created = $true");
            // Le module GroupPolicy ne possède pas de cmdlet « Set-GPO » :
            // la description se met à jour directement sur l'objet GPO retourné
            // par Get-GPO (écriture immédiate dans AD DS).
            script.AppendLine("} elseif ('" + Escape(Comment ?? string.Empty) + "'.Length -gt 0) {");
            script.AppendLine("  $gpo.Description = '" + Escape(Comment ?? string.Empty) + "'");
            script.AppendLine("}");

            // Nettoyage des listes indexées : une valeur « 1..N » supprimée
            // doit disparaître de la GPO, sinon l'ancienne URL resterait bloquée.
            var indexedKeys = Settings
                .Where(setting => int.TryParse(setting.ValueName, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                .Select(setting => setting.Key)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var key in indexedKeys)
            {
                script.AppendLine(
                    "for ($i = 1; $i -le " + MaxIndexedValues + "; $i++) { "
                    + "Remove-GPRegistryValue -Guid $gpo.Id -Key '" + Escape(key) + "' -ValueName ([string]$i) -ErrorAction SilentlyContinue | Out-Null }");
            }

            var cleaned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var setting in Settings)
            {
                var key = Escape(setting.Key);
                var valueName = Escape(setting.ValueName);
                var identity = setting.Key + "\u0001" + setting.ValueName;

                if (cleaned.Add(identity))
                {
                    script.AppendLine(
                        "Remove-GPRegistryValue -Guid $gpo.Id -Key '" + key + "' -ValueName '" + valueName
                        + "' -ErrorAction SilentlyContinue | Out-Null");
                }

                script.AppendLine(
                    "Set-GPRegistryValue -Guid $gpo.Id -Key '" + key + "' -ValueName '" + valueName
                    + "' -Type " + setting.ValueType + " -Value " + FormatValue(setting) + " -ErrorAction Stop | Out-Null");
            }

            script.AppendLine("$gpo = Get-GPO -Guid $gpo.Id -ErrorAction Stop");
            script.AppendLine("[PSCustomObject]@{ GpoId = $gpo.Id.ToString(); DisplayName = $gpo.DisplayName; Description = $gpo.Description; Created = $created }");

            return script.ToString();
        }
    }

    private static string FormatValue(GpoRegistrySetting setting)
    {
        return setting.ValueType.ToUpperInvariant() switch
        {
            "DWORD" => " " + ParseNumber(setting).ToString(CultureInfo.InvariantCulture),
            "QWORD" => " " + ParseNumber(setting).ToString(CultureInfo.InvariantCulture),
            "MULTISTRING" => " @(" + string.Join(", ", SplitLines(setting.Value).Select(value => "'" + Escape(value) + "'")) + ")",
            "BINARY" => " ([byte[]](" + FormatBinary(setting.Value) + "))",
            "EXPANDSTRING" => " '" + Escape(setting.Value) + "'",
            _ => " '" + Escape(setting.Value) + "'",
        };
    }

    private static long ParseNumber(GpoRegistrySetting setting)
    {
        if (!long.TryParse(setting.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            throw new ArgumentException(
                "La valeur « " + setting.Value + " » n'est pas un nombre valide pour la valeur de registre "
                + setting.Key + "\\" + setting.ValueName + ".");
        }

        return number;
    }

    internal static IReadOnlyList<string> SplitLines(string value)
        => value.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

    private static string FormatBinary(string value)
    {
        var normalized = value.Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase);
        var bytes = new List<byte>();
        foreach (var part in normalized.Split(new[] { ',', ' ', ';', '-' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!byte.TryParse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
            {
                throw new ArgumentException("Valeur binaire invalide : « " + value + " » (hexadécimal attendu).");
            }

            bytes.Add(parsed);
        }

        return string.Join(", ", bytes.Select(item => item.ToString(CultureInfo.InvariantCulture)));
    }

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
