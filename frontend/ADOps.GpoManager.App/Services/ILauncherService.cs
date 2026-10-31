using System.Diagnostics;
using System.IO;

namespace ADOps.GpoManager.App.Services;

public interface ILauncherService
{
    void OpenFolder(string path);
}

public sealed class LauncherService : ILauncherService
{
    public void OpenFolder(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
