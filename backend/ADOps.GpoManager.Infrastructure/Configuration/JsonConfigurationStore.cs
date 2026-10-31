using System.Text.Json;
using ADOps.GpoManager.Infrastructure.Storage;

namespace ADOps.GpoManager.Infrastructure.Configuration;

/// <summary>Persistance de la configuration au format JSON.</summary>
public sealed class JsonConfigurationStore : IConfigurationStore
{
    private readonly string _filePath;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public JsonConfigurationStore(StoragePaths paths)
    {
        _filePath = paths.ConfigurationFile;
    }

    public AppConfiguration Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new AppConfiguration();
            }

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AppConfiguration>(json) ?? new AppConfiguration();
        }
        catch (JsonException)
        {
            return new AppConfiguration();
        }
        catch (IOException)
        {
            return new AppConfiguration();
        }
    }

    /// <summary>
    /// Vrai si le fichier de configuration existe déjà. Utilisé pour le créer
    /// au premier démarrage : le fichier est le panneau de configuration
    /// lisible et éditable de la cible du domaine (contrôleur, adresse, domaine).
    /// </summary>
    public bool FileExists => File.Exists(_filePath);

    public void Save(AppConfiguration configuration)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(configuration, SerializerOptions);
        File.WriteAllText(_filePath, json);
    }
}
