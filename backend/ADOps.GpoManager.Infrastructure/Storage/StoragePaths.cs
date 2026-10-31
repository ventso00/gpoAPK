namespace ADOps.GpoManager.Infrastructure.Storage;

/// <summary>Chemins de stockage local : %LOCALAPPDATA%\ADOps\GpoManager.</summary>
public sealed class StoragePaths
{
    public static StoragePaths Default { get; } = new(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public string Root { get; }
    public string LogDirectory { get; }
    public string DatabaseFile { get; }
    public string ConfigurationFile { get; }

    public StoragePaths(string baseDirectory)
    {
        Root = Path.Combine(baseDirectory, "ADOps", "GpoManager");
        LogDirectory = Path.Combine(Root, "logs");
        DatabaseFile = Path.Combine(Root, "audit.db");
        ConfigurationFile = Path.Combine(Root, "settings.json");

        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogDirectory);
    }
}
