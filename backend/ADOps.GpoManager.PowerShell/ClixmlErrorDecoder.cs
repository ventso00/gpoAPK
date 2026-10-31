using System.Text;
using System.Text.RegularExpressions;

namespace ADOps.GpoManager.PowerShell;

/// <summary>
/// Décode le flux d'erreur sérialisé que powershell.exe écrit sur son stderr
/// quand une erreur est produite dans un canal de redirection (native command
/// avec <c>2&gt;&amp;1</c>, bloc de script <c>Invoke-Command</c>…) :
/// <code>
/// #&lt; CLIXML
/// &lt;Objs …&gt;&lt;S S="Error"&gt;…&lt;/S&gt;&lt;/Objs&gt;
/// </code>
/// Sans décodage, l'interface n'affiche que l'en-tête <c>#&lt; CLIXML</c> et les
/// règles de traduction de l'<see cref="ErrorInterpreter"/> ne reconnaissent
/// aucun motif. Le texte réellement utile est dans les enregistrements
/// <c>S S="Error"</c> ; les enregistrements <c>Progress</c> sont ignorés.
/// </summary>
public static class ClixmlErrorDecoder
{
    private const string Marker = "#< CLIXML";

    private static readonly Regex Records = new(
        "<S S=\"(?:Error|Failure)\"[^>]*>(?<text>.*?)</S>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>Vrai si le texte contient une enveloppe CLIXML sérialisée.</summary>
    public static bool LooksLikeClixml(string? value)
        => value is not null && value.Contains(Marker, StringComparison.Ordinal);

    /// <summary>
    /// Retourne les messages d'erreur lisibles extraits du flux, ou le texte
    /// d'origine s'il ne s'agit pas d'une enveloppe CLIXML.
    /// </summary>
    public static string Decode(string? stderr)
    {
        if (!LooksLikeClixml(stderr))
        {
            return stderr ?? string.Empty;
        }

        var messages = new List<string>();
        foreach (Match match in Records.Matches(stderr!))
        {
            var text = Unescape(match.Groups["text"].Value);
            if (!string.IsNullOrWhiteSpace(text))
            {
                messages.Add(text.Trim());
            }
        }

        if (messages.Count == 0)
        {
            return stderr;
        }

        // Les enregistrements successifs forment souvent un seul message coupé :
        // la concaténation préserve plus d'information que la seule première ligne.
        var builder = new StringBuilder();
        foreach (var message in messages)
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(message);
        }

        return builder.ToString();
    }

    private static string Unescape(string value)
    {
        var decoded = value
            .Replace("&lt;", "<", StringComparison.Ordinal)
            .Replace("&gt;", ">", StringComparison.Ordinal)
            .Replace("&quot;", "\"", StringComparison.Ordinal)
            .Replace("&apos;", "'", StringComparison.Ordinal)
            .Replace("&amp;", "&", StringComparison.Ordinal);

        // Powershell escape les retours chariot/ ligne du message d'origine.
        return decoded
            .Replace("_x000D_", string.Empty, StringComparison.Ordinal)
            .Replace("_x000A_", "\n", StringComparison.Ordinal);
    }
}
