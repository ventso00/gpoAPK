using System.Globalization;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using ADOps.GpoManager.Core.Security;
using ADOps.GpoManager.Core.Services;

namespace ADOps.GpoManager.Infrastructure.Logging;

/// <summary>
/// Journal texte durci :
/// - rédaction systématique des secrets et identifiants (jamais de mot de passe,
///   de blob DPAPI, de clé ou d'e-mail en clair) ;
/// - chaîne de hachage par ligne (preuve d'intégrité : toute altération ou
///   suppression d'une ligne est détectable par <see cref="VerifyIntegrity"/>) ;
/// - ACL restrictive sur le fichier (utilisateur courant, SYSTEM, Administrateurs).
/// Rotation par taille conservée ; les archives portent la même chaîne.
/// </summary>
public sealed class FileLogger : ILogger, IDisposable
{
    private readonly string _logDirectory;
    private readonly long _maxFileSizeBytes;
    private readonly int _filesToKeep;
    private readonly bool _sanitize;
    private readonly bool _integrity;
    private readonly object _gate = new();
    private readonly UTF8Encoding _encoding = new(encoderShouldEmitUTF8Identifier: false);

    private StreamWriter? _writer;
    private string? _currentFile;
    private long _currentFileSize;
    private string _previousHash = "GENESIS";
    private bool _disposed;

    public FileLogger(
        string logDirectory,
        int maxFileSizeMb = 5,
        int filesToKeep = 5,
        bool sanitize = true,
        bool integrity = true)
    {
        _logDirectory = logDirectory;
        _maxFileSizeBytes = maxFileSizeMb * 1024L * 1024L;
        _filesToKeep = filesToKeep;
        _sanitize = sanitize;
        _integrity = integrity;
    }

    public void Trace(string message, Exception? exception = null) => Write(LogSeverity.Trace, message, exception);
    public void Debug(string message, Exception? exception = null) => Write(LogSeverity.Debug, message, exception);
    public void Information(string message, Exception? exception = null) => Write(LogSeverity.Information, message, exception);
    public void Warning(string message, Exception? exception = null) => Write(LogSeverity.Warning, message, exception);
    public void Error(string message, Exception? exception = null) => Write(LogSeverity.Error, message, exception);
    public void Critical(string message, Exception? exception = null) => Write(LogSeverity.Critical, message, exception);

    private void Write(LogSeverity severity, string message, Exception? exception)
    {
        try
        {
            var safeMessage = _sanitize ? LogSanitizer.Sanitize(message) ?? string.Empty : message;
            var safeException = exception is null
                ? string.Empty
                : " | " + (_sanitize ? LogSanitizer.SanitizeException(exception) : exception.ToString());

            var body = string.Format(
                CultureInfo.InvariantCulture,
                "{0:yyyy-MM-ddTHH:mm:ss.fffZ} [{1}] {2}{3}",
                DateTime.UtcNow,
                severity,
                safeMessage,
                safeException);

            lock (_gate)
            {
                EnsureWriter();
                var line = _integrity ? ApplyChain(body) : body;
                _writer!.WriteLine(line);
                _writer.Flush();
                _currentFileSize += _encoding.GetByteCount(line) + Environment.NewLine.Length;
                if (_currentFileSize >= _maxFileSizeBytes)
                {
                    Rotate();
                }
            }
        }
        catch
        {
            // Le journal ne doit jamais interrompre l'application.
        }
    }

    /// <summary>Ajoute l'empreinte de la ligne précédente (détection d'altération).</summary>
    private string ApplyChain(string body)
    {
        var hash = ComputeHash(_previousHash + "|" + body);
        _previousHash = hash;
        return body + " [h=" + hash[..12] + "]";
    }

    private static string ComputeHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }

    private void EnsureWriter()
    {
        if (_writer is not null)
        {
            return;
        }

        Directory.CreateDirectory(_logDirectory);
        _currentFile = Path.Combine(_logDirectory, "adops.log");
        if (File.Exists(_currentFile))
        {
            _currentFileSize = new FileInfo(_currentFile).Length;
            if (_integrity)
            {
                SeedPreviousHash(_currentFile);
            }
        }
        else
        {
            _currentFileSize = 0;
        }

        _writer = new StreamWriter(_currentFile, append: true, _encoding);
        if (_currentFileSize == 0)
        {
            RestrictFilePermissions(_currentFile);
        }
    }

    /// <summary>Lit la dernière empreinte du fichier existant pour continuer la chaîne.</summary>
    private void SeedPreviousHash(string path)
    {
        try
        {
            string? lastLine = null;
            foreach (var line in File.ReadLines(path, _encoding))
            {
                lastLine = line;
            }

            if (lastLine is not null)
            {
                var marker = lastLine.LastIndexOf("[h=", StringComparison.Ordinal);
                if (marker >= 0 && lastLine.EndsWith("]", StringComparison.Ordinal))
                {
                    _previousHash = lastLine[(marker + 3)..^1];
                }
            }
        }
        catch (IOException)
        {
            // Fichier momentanément verrouillé : la chaîne repart de GENESIS.
        }
    }

    /// <summary>ACL restrictive : retour au contrôle total supprimé, seuls les rôles légitimes conservés.</summary>
    private static void RestrictFilePermissions(string path)
    {
        try
        {
            var currentUser = WindowsIdentity.GetCurrent().User;
            if (currentUser is null)
            {
                return;
            }

            var security = new FileSecurity(path, AccessControlSections.Access);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(
                currentUser, FileSystemRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.ReadAndExecute, AccessControlType.Allow));

            new FileInfo(path).SetAccessControl(security);
        }
        catch
        {
            // Plateforme ou droits insuffisants : la rédaction et la chaîne restent actives.
        }
    }

    /// <summary>
    /// Vérifie l'intégrité d'un fichier journal (chaîne de hachage).
    /// Retourne le numéro de la première ligne altérée, ou null si le fichier est intègre.
    /// </summary>
    public static long? VerifyIntegrity(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var previous = "GENESIS";
        var lineNumber = 0L;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            var markerIndex = line.LastIndexOf("[h=", StringComparison.Ordinal);
            if (markerIndex < 0 || !line.EndsWith("]", StringComparison.Ordinal))
            {
                return lineNumber;
            }

            var expected = line[(markerIndex + 3)..^1];
            var body = line[..markerIndex].TrimEnd();
            var actual = ComputeHash(previous + "|" + body)[..12];
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                return lineNumber;
            }

            previous = expected;
        }

        return null;
    }

    private void Rotate()
    {
        _writer!.Dispose();
        _writer = null;

        if (_currentFile is not null && File.Exists(_currentFile))
        {
            var archive = Path.Combine(_logDirectory, "adops-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".log");
            File.Move(_currentFile, archive);
        }

        var obsolete = Directory.GetFiles(_logDirectory, "adops-*.log")
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .Skip(_filesToKeep);

        foreach (var file in obsolete)
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Fichier verrouillé : il sera purgé à la prochaine rotation.
            }
        }

        _previousHash = "GENESIS";
        _currentFileSize = 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
