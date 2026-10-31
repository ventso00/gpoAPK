using System.Security.Cryptography;
using System.Text.Json;

namespace ADOps.GpoManager.Core.Security;

/// <summary>
/// Détection d'altération des binaires de l'application (anti-tampering).
/// Au moment de la publication, un manifeste d'intégrité (SHA-256 par fichier)
/// est généré à côté de l'exécutable ; au démarrage, l'application recalcule
/// les empreintes et signale tout écart (binaire remplacé, patché, ou
/// bibliothèque substituée). Ce mécanisme DÉTECTE l'altération — il ne
/// remplace pas l'obfuscation (recommandations dans docs/04).
/// </summary>
public static class AssemblyIntegrityGuard
{
    public const string ManifestFileName = "integrity.manifest";

    /// <summary>Fichiers surveillés (exécutable et bibliothèques de la solution).</summary>
    private static readonly string[] WatchedExtensions = { ".exe", ".dll", ".runtimeconfig.json" };

    /// <summary>Génère le manifeste d'intégrité pour un répertoire de publication.</summary>
    public static void CreateManifest(string directory)
    {
        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(directory))
        {
            var extension = Path.GetExtension(file);
            if (!WatchedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            entries[Path.GetFileName(file)] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
        }

        var manifest = new { GeneratedUtc = DateTimeOffset.UtcNow, Files = entries };
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(directory, ManifestFileName), json);
    }

    /// <summary>
    /// Vérifie les binaires du répertoire contre le manifeste.
    /// Retourne la liste des fichiers altérés/manquants (vide = intègre).
    /// Si le manifeste est absent (exécution en développement), la vérification est ignorée.
    /// </summary>
    public static IReadOnlyList<string> VerifyDirectory(string directory)
    {
        var manifestPath = Path.Combine(directory, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return Array.Empty<string>();
        }

        var tampered = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (!document.RootElement.TryGetProperty("Files", out var files))
            {
                return Array.Empty<string>();
            }

            foreach (var file in files.EnumerateObject())
            {
                var path = Path.Combine(directory, file.Name);
                if (!File.Exists(path))
                {
                    tampered.Add(file.Name + " (manquant)");
                    continue;
                }

                var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
                if (!string.Equals(actual, file.Value.GetString(), StringComparison.OrdinalIgnoreCase))
                {
                    tampered.Add(file.Name + " (empreinte modifiée)");
                }
            }
        }
        catch (JsonException)
        {
            return new[] { ManifestFileName + " (illisible)" };
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }

        return tampered;
    }

    /// <summary>Répertoire de l'assembly courant (emplacement de l'exécutable).</summary>
    public static string CurrentDirectory()
        => Path.GetDirectoryName(typeof(AssemblyIntegrityGuard).Assembly.Location) ?? AppContext.BaseDirectory;
}
