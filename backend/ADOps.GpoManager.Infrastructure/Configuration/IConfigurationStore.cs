namespace ADOps.GpoManager.Infrastructure.Configuration;

public interface IConfigurationStore
{
    AppConfiguration Load();
    void Save(AppConfiguration configuration);
}
