using ADOps.GpoManager.Templates;
using Xunit;

namespace ADOps.GpoManager.Tests;

public class GpoScriptCatalogTests
{
    [Fact]
    public void Catalog_LoadsEmbeddedScripts()
    {
        var catalog = new GpoScriptCatalog();
        Assert.NotEmpty(catalog.GetIds());
        Assert.Contains("control-panel-settings", catalog.GetIds());
        var script = catalog.GetFullScript("control-panel-settings");
        Assert.False(string.IsNullOrWhiteSpace(script));
        Assert.Contains("New-GPO", script!);
        Assert.Contains("Set-GPRegistryValue", script!);
        Assert.Contains("New-GPLink", script!);
    }
}
