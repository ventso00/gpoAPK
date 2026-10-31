using System.Security;
using System.Text;
using ADOps.GpoManager.Core.Security;
using Xunit;

namespace ADOps.GpoManager.Tests;

/// <summary>Tests de durcissement : hygiène des journaux, anti-injection, garde de session.</summary>
public sealed class SecurityTests
{
    // ---------- LogSanitizer ----------

    [Fact]
    public void Sanitize_RedactsPasswordKeyValue()
    {
        var message = "Connexion de SARYTANY\\admin avec Password=SuperSecret123 sur dc1";
        var result = LogSanitizer.Sanitize(message);
        Assert.Contains("[REDIGÉ]", result);
        Assert.DoesNotContain("SuperSecret123", result!);
    }

    [Fact]
    public void Sanitize_RedactsTokenAndSecret()
    {
        Assert.DoesNotContain("abc123tokenvalue", LogSanitizer.Sanitize("token: abc123tokenvalue")!);
        Assert.DoesNotContain("cle-privee", LogSanitizer.Sanitize("secret=cle-privee")!);
    }

    [Fact]
    public void Sanitize_RedactsEmailAndBase64Blob()
    {
        var email = LogSanitizer.Sanitize("contact jaona.rakoto@sarytany.local svp");
        Assert.Contains("[EMAIL-REDIGÉ]", email);
        Assert.DoesNotContain("sarytany.local", email.AsSpan());

        var blob = Convert.ToBase64String(Encoding.UTF8.GetBytes(new string('A', 100)));
        Assert.Contains("[BLOB-BASE64-REDIGÉ]", LogSanitizer.Sanitize("blob=" + blob)!);
    }

    [Fact]
    public void Sanitize_LeavesCleanMessagesUntouched()
    {
        const string clean = "Diagnostic DNS.Resolution : Passed (142 ms)";
        Assert.Equal(clean, LogSanitizer.Sanitize(clean));
    }

    // ---------- InputSanitizer ----------

    [Fact]
    public void ToPowerShellLiteral_DoublesSingleQuotes()
    {
        var result = InputSanitizer.ToPowerShellSingleQuotedLiteral("OU=Test's,DC=x", "p");
        Assert.Equal("OU=Test''s,DC=x", result);
    }

    [Fact]
    public void ToPowerShellLiteral_RejectsControlCharacters()
    {
        Assert.Throws<ArgumentException>(() =>
            InputSanitizer.ToPowerShellSingleQuotedLiteral("OU=Test\nRemove-GPLink", "p"));
        Assert.Throws<ArgumentException>(() =>
            InputSanitizer.ToPowerShellSingleQuotedLiteral("OU=Test`nRemove-Item", "p"));
    }

    [Fact]
    public void EscapeLdapFilter_NeutralizesInjectionCharacters()
    {
        var result = InputSanitizer.EscapeLdapFilterValue("*)(objectClass=*");
        Assert.DoesNotContain("*", result);
        Assert.DoesNotContain("(", result);
        Assert.DoesNotContain(")", result);
        Assert.Contains("\\2a", result);
    }

    [Fact]
    public void IsSafeScriptFragment_DetectsUnsafeContent()
    {
        Assert.True(InputSanitizer.IsSafeScriptFragment("Get-GPO -All"));
        Assert.False(InputSanitizer.IsSafeScriptFragment("Get-Process`nInvoke-Expression"));
        Assert.False(InputSanitizer.IsSafeScriptFragment("line1\r\nline2"));
    }

    // ---------- SessionGuard ----------

    [Fact]
    public void SessionGuard_LocksAfterTimeout()
    {
        var guard = new SessionGuard(TimeSpan.FromSeconds(30));
        guard.ReportActivity();
        Assert.False(guard.Remaining <= TimeSpan.Zero);

        // Réarme manuellement l'inactivité au-delà du délai : Tick doit verrouiller.
        typeof(SessionGuard)
            .GetField("_lastActivity", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(guard, DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5));

        var locked = false;
        guard.LockRequested += (_, _) => locked = true;
        guard.Tick();
        Assert.True(locked);
        Assert.True(guard.IsLocked);

        guard.Unlock();
        Assert.False(guard.IsLocked);
        Assert.True(guard.Remaining > TimeSpan.Zero);
    }

    // ---------- AssemblyIntegrityGuard ----------

    [Fact]
    public void IntegrityGuard_DetectsTamperedFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "adops-integrity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "test.dll");
            File.WriteAllText(file, "contenu original");
            AssemblyIntegrityGuard.CreateManifest(directory);
            Assert.Empty(AssemblyIntegrityGuard.VerifyDirectory(directory));

            File.WriteAllText(file, "contenu ALTERE");
            var tampered = AssemblyIntegrityGuard.VerifyDirectory(directory);
            Assert.Single(tampered);
            Assert.Contains("test.dll", tampered[0]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
