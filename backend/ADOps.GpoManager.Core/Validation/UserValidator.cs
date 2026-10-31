namespace ADOps.GpoManager.Core.Validation;

/// <summary>Validation des noms d'utilisateur : DOMAINE\utilisateur ou UPN (utilisateur@domaine.com).</summary>
public static class UserValidator
{
    /// <summary>Valide un nom d'utilisateur au format NetBIOS (DOMAINE\utilisateur) ou UPN.</summary>
    public static (bool IsValid, string? Error) ValidateUserName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (false, "Le nom d'utilisateur est requis.");
        }

        var trimmed = value.Trim();

        if (trimmed.Contains('\\'))
        {
            var parts = trimmed.Split('\\');
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            {
                return (false, "Format attendu : DOMAINE\\utilisateur (ex. : VENTSO\\administrateur).");
            }

            return (true, null);
        }

        var atIndex = trimmed.IndexOf('@');
        if (atIndex > 0 && atIndex == trimmed.LastIndexOf('@'))
        {
            var localPart = trimmed[..atIndex];
            var domainPart = trimmed[(atIndex + 1)..];
            if (!string.IsNullOrWhiteSpace(localPart)
                && domainPart.Contains('.')
                && domainPart.Length >= 3
                && !domainPart.StartsWith('.')
                && !domainPart.EndsWith('.'))
            {
                return (true, null);
            }
        }

        return (false, "Format attendu : DOMAINE\\utilisateur ou utilisateur@domaine.com (UPN).");
    }
}
