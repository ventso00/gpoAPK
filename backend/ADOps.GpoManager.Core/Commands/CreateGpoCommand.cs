using System.Globalization;
using ADOps.GpoManager.Core.Services;
using ADOps.GpoManager.Core.Validation;

namespace ADOps.GpoManager.Core.Commands;

/// <summary>
/// Crée une GPO dans le domaine à partir d'un modèle du catalogue, puis
/// applique ses paramètres de registre.
///
/// <para>
/// Sert à l'option « Ajouter une GPO » : l'administrateur décrit la stratégie
/// voulue (nom, chemin de registre, valeur, type) et la GPO est réellement
/// créée dans le domaine. Toutes les valeurs sont validées avant injection :
/// le chemin de registre est un nom de clé contrôlé, la valeur est échappée.
/// </para>
/// </summary>
public sealed class CreateGpoCommand : PowerShellCommand
{
    /// <summary>Nom de la GPO à créer.</summary>
    public string GpoName { get; }

    /// <summary>Commentaire descriptif.</summary>
    public string? Comment { get; }

    /// <summary>Paramètres de registre appliqués à la GPO.</summary>
    public IReadOnlyList<GpoRegistrySetting> Settings { get; }

    public override string Name => "CreateGpo";
    public override TimeSpan Timeout { get; } = TimeSpan.FromMinutes(3);

    public CreateGpoCommand(
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

        Settings = settings ?? Array.Empty<GpoRegistrySetting>();

        foreach (var setting in Settings)
        {
            setting.Validate();
        }

        GpoName = gpoName.Trim();
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
    }

    public override string ScriptBody
    {
        get
        {
            var name = EscapeLiteral(GpoName);
            var comment = Comment is null ? string.Empty : " -Comment '" + EscapeLiteral(Comment) + "'";
            var script =
                "Import-Module GroupPolicy -ErrorAction Stop\n" +
                "$gpo = New-GPO -Name '" + name + "'" + comment + " -ErrorAction Stop\n";

            foreach (var setting in Settings)
            {
                script +=
                    "Set-GPRegistryValue -Guid $gpo.Id -Key '" + EscapeLiteral(setting.Key) + "'" +
                    " -ValueName '" + EscapeLiteral(setting.ValueName) + "'" +
                    " -Type " + setting.ValueType +
                    " -Value " + FormatValue(setting) +
                    " -ErrorAction Stop\n";
            }

            script +=
                "$gpo = Get-GPO -Guid $gpo.Id -ErrorAction Stop\n" +
                "[PSCustomObject]@{ GpoId = $gpo.Id.ToString(); DisplayName = $gpo.DisplayName; Description = $gpo.Description }";

            return script;
        }
    }

    private static string FormatValue(GpoRegistrySetting setting)
    {
        return setting.ValueType switch
        {
            "DWord" => " " + setting.Value.ToString(CultureInfo.InvariantCulture),
            "QWord" => " " + setting.Value.ToString(CultureInfo.InvariantCulture),
            "MultiString" => " @(" + string.Join(", ", ApplyGpoTemplateCommand.SplitLines(setting.Value).Select(value => "'" + EscapeLiteral(value) + "'")) + ")",
            "ExpandString" => " '" + EscapeLiteral(setting.Value) + "'",
            _ => " '" + EscapeLiteral(setting.Value) + "'",
        };
    }

    private static string EscapeLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}

/// <summary>Paramètre de registre appliqué par une GPO.</summary>
public sealed class GpoRegistrySetting
{
    /// <summary>Chemin de la clé de registre (sans la racine HKCU/HKLM).</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Nom de la valeur.</summary>
    public string ValueName { get; init; } = string.Empty;

    /// <summary>Type ADSI (String, ExpandString, Binary, DWord, QWord, MultiString).</summary>
    public string ValueType { get; init; } = "String";

    /// <summary>Valeur associée.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>Valide la cohérence du paramètre avant toute injection.</summary>
    public void Validate()
    {
        TemplateMaterializer.ValidateRegistryIdentity(Key, ValueName, ValueType);

        if (Value.Length > 65535)
        {
            throw new ArgumentException("La valeur de registre dépasse 65 535 caractères.", nameof(Value));
        }

        if (ValueType.Equals("String", StringComparison.OrdinalIgnoreCase)
            || ValueType.Equals("ExpandString", StringComparison.OrdinalIgnoreCase)
            || ValueType.Equals("DWord", StringComparison.OrdinalIgnoreCase)
            || ValueType.Equals("QWord", StringComparison.OrdinalIgnoreCase))
        {
            if (Value.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            {
                throw new ArgumentException(
                    "Une valeur " + ValueType + " ne peut pas contenir de saut de ligne (" + Key + "\\" + ValueName + ").",
                    nameof(Value));
            }
        }
    }
}
