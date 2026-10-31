using System.Security;
using ADOps.GpoManager.Core.Validation;

namespace ADOps.GpoManager.Core.Models;

/// <summary>
/// Identifiants de connexion au domaine.
/// Contraintes de sécurité (cahier des charges §4.4) :
/// - le mot de passe n'est jamais journalisé, stocké (SQLite/settings.json) ni passé en argument de processus ;
/// - il ne vit qu'en mémoire, le temps de la session, puis est effacé (Dispose).
/// </summary>
public sealed class ConnectionCredentials : IDisposable
{
    private readonly SecureString? _password;
    private bool _disposed;

    public CredentialMode Mode { get; }

    /// <summary>Nom d'utilisateur (DOMAINE\utilisateur ou UPN). N'est pas un secret.</summary>
    public string? UserName { get; }

    public ConnectionCredentials()
    {
        Mode = CredentialMode.CurrentUser;
        UserName = null;
        _password = null;
    }

    public ConnectionCredentials(string userName, SecureString password)
    {
        var (isValid, error) = UserValidator.ValidateUserName(userName);
        if (!isValid)
        {
            throw new ArgumentException(error, nameof(userName));
        }

        if (password is null || password.Length == 0)
        {
            throw new ArgumentException("Le mot de passe est requis.", nameof(password));
        }

        Mode = CredentialMode.Explicit;
        UserName = userName.Trim();
        _password = password.Copy();
    }

    /// <summary>Copie protégée du mot de passe, ou null en mode compte courant.</summary>
    public SecureString? GetPassword()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _password?.Copy();
    }

    /// <summary>Représentation affichable : jamais le mot de passe.</summary>
    public string ToDisplayString()
        => Mode == CredentialMode.CurrentUser ? "Compte Windows actuel" : UserName!;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _password?.Dispose();
    }
}
