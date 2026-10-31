using System.Text.RegularExpressions;

namespace ADOps.GpoManager.Core.Security;

/// <summary>
/// Hygiène de journalisation : neutralise tout secret ou identifiant personnel
/// AVANT écriture dans les journaux (fichier, base d'audit, affichage).
/// Objectif : un journal peut être lu par un exploitant, pas par un admin
/// devant y retrouver un mot de passe — la rédaction est donc systématique.
/// </summary>
public static partial class LogSanitizer
{
    private const string Redacted = "[REDIGÉ]";

    private static readonly Regex KeyValueSecret = new(
        @"(?i)\b(password|passwd|pwd|mot[_\s]?de[_\s]?passe|secret|token|api[_-]?key|apikey|credential[s]?|authorization|bearer)\b\s*[=:]\s*(""[^""]*""|'[^']*'|\S+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex EmailLike = new(
        @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LongBase64 = new(
        @"\b[A-Za-z0-9+/]{40,}={0,2}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LongHex = new(
        @"\b[0-9a-fA-F]{32,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Rédige les secrets et identifiants personnels d'un message.</summary>
    public static string? Sanitize(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return message;
        }

        var result = KeyValueSecret.Replace(message, "$1=" + Redacted);
        result = LongBase64.Replace(result, "[BLOB-BASE64-REDIGÉ]");
        result = LongHex.Replace(result, "[CLÉ-HEXA-REDIGÉE]");
        result = EmailLike.Replace(result, "[EMAIL-REDIGÉ]");
        return result;
    }

    /// <summary>Rédige une exception (message + pile) — y compris les exceptions internes.</summary>
    public static string SanitizeException(Exception? exception)
    {
        if (exception is null)
        {
            return string.Empty;
        }

        var current = exception;
        var parts = new List<string>();
        while (current is not null)
        {
            parts.Add(current.GetType().Name + ": " + (Sanitize(current.Message) ?? string.Empty));
            current = current.InnerException;
        }

        return string.Join(" <- ", parts)
            + (exception.StackTrace is null ? string.Empty : " | " + (Sanitize(exception.StackTrace) ?? string.Empty));
    }

    /// <summary>Rédige n'importe quel objet (utilisé pour les champs libres d'audit).</summary>
    public static string? Sanitize(object? value) => value switch
    {
        null => null,
        string text => Sanitize(text),
        Exception exception => SanitizeException(exception),
        _ => Sanitize(value.ToString()),
    };

    /// <summary>
    /// Vrai si le message contient un motif sensible (permet de tester la
    /// rédaction dans les tests unitaires et d'alerter en mode Debug).
    /// </summary>
    public static bool ContainsSensitivePattern(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        return KeyValueSecret.IsMatch(message)
            || LongBase64.IsMatch(message)
            || EmailLike.IsMatch(message);
    }
}
