using System.Security;

namespace ADOps.GpoManager.Core.Models;

/// <summary>Identifiants en transit entre l'application et l'exécuteur PowerShell distant.</summary>
public sealed record RemoteCredentialHandoff
{
    public string UserName { get; init; } = string.Empty;

    /// <summary>Blob DPAPI (portée utilisateur courant) du mot de passe — jamais le mot de passe en clair.</summary>
    public string ProtectedPasswordBlob { get; init; } = string.Empty;

    public static RemoteCredentialHandoff FromPassword(string userName, SecureString password)
        => new() { UserName = userName, ProtectedPasswordBlob = DpApiProtector.Protect(password) };

    /// <summary>Reconstruit un SecureString à partir du blob DPAPI (même utilisateur, même machine).</summary>
    public SecureString UnprotectPassword() => DpApiProtector.Unprotect(ProtectedPasswordBlob);
}

/// <summary>Protection DPAPI locale (portée utilisateur courant) pour le transit de secrets.</summary>
internal static class DpApiProtector
{
    public static string Protect(SecureString password)
    {
        var ptr = IntPtr.Zero;
        try
        {
            ptr = System.Runtime.InteropServices.Marshal.SecureStringToGlobalAllocUnicode(password);
            var plain = new byte[password.Length * 2];
            System.Runtime.InteropServices.Marshal.Copy(ptr, plain, 0, plain.Length);
            var protectedBytes = System.Security.Cryptography.ProtectedData.Protect(
                plain, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }
        finally
        {
            if (ptr != IntPtr.Zero)
            {
                System.Runtime.InteropServices.Marshal.ZeroFreeGlobalAllocUnicode(ptr);
            }
        }
    }

    public static SecureString Unprotect(string protectedBlob)
    {
        var protectedBytes = Convert.FromBase64String(protectedBlob);
        var plainBytes = System.Security.Cryptography.ProtectedData.Unprotect(
            protectedBytes, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);

        var secure = new SecureString();
        var chars = System.Text.Encoding.Unicode.GetChars(plainBytes);
        foreach (var c in chars)
        {
            secure.AppendChar(c);
        }

        Array.Clear(plainBytes);
        return secure;
    }
}
