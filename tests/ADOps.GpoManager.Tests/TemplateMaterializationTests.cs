using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Templates;
using Xunit;

namespace ADOps.GpoManager.Tests;

/// <summary>
/// Preuve exécutable que les 16 modèles du catalogue ne sont pas de simples
/// fiches documentaires : chacun produit des écritures de registre valides
/// avec ses valeurs par défaut, et pour chaque valeur d'énumération possible.
/// </summary>
public class TemplateMaterializationTests
{
    private static TemplateCatalog CreateCatalog() => new();

    [Fact]
    public void EveryCatalogTemplate_HasAnExecutableBinding()
    {
        var catalog = CreateCatalog();

        foreach (var template in catalog.GetTemplates())
        {
            var binding = catalog.GetBinding(template.TemplateId);
            Assert.True(binding is not null, "Aucune liaison de registre pour " + template.TemplateId);
            Assert.Equal(template.TemplateId, binding!.TemplateId);
            Assert.NotEmpty(binding.Entries);
        }
    }

    [Fact]
    public void EveryCatalogTemplate_MaterializesFromDefaults()
    {
        var catalog = CreateCatalog();

        foreach (var template in catalog.GetTemplates())
        {
            var settings = catalog.Materialize(template.TemplateId, new Dictionary<string, object?>());

            Assert.True(settings.Count > 0, template.TemplateId + " ne produit aucune valeur de registre.");
            foreach (var setting in settings)
            {
                Assert.StartsWith("HK", setting.Key, StringComparison.OrdinalIgnoreCase);
                Assert.True(setting.Key.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase)
                            || setting.Key.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase),
                    template.TemplateId + " : racine de registre non autorisée " + setting.Key);
                Assert.False(string.IsNullOrWhiteSpace(setting.ValueName), template.TemplateId);
                Assert.Contains(setting.ValueType, new[] { "String", "ExpandString", "DWord", "QWord", "MultiString", "Binary" });
            }
        }
    }

    [Fact]
    public void EveryEnumValue_MaterializesWithoutError()
    {
        var catalog = CreateCatalog();

        foreach (var template in catalog.GetTemplates())
        {
            foreach (var parameter in template.Parameters.Where(p => p.Type == TemplateParameterType.Enum))
            {
                foreach (var value in parameter.EnumValues ?? Array.Empty<string>())
                {
                    var parameters = new Dictionary<string, object?> { [parameter.Name] = value };
                    var settings = catalog.Materialize(template.TemplateId, parameters);
                    Assert.True(settings.Count > 0,
                        template.TemplateId + " / " + parameter.Name + "=" + value + " ne produit aucun réglage.");
                }
            }
        }
    }

    [Fact]
    public void EmptyMaterialization_FailsInsteadOfSilentlyCreatingAnEmptyGpo()
    {
        // Si un jeu de paramètres ne produit plus aucun réglage, mieux vaut
        // refuser le déploiement que créer une GPO vide présentée comme réussie.
        Assert.Throws<ArgumentException>(() => CreateCatalog().Materialize(
            "control-panel-settings",
            new Dictionary<string, object?> { ["blockControlPanel"] = false }));
    }

    [Fact]
    public void MultilineLists_AreExpandedWithSequentialNames()
    {
        var catalog = CreateCatalog();

        var settings = catalog.Materialize("browser-url-blocklist", new Dictionary<string, object?>
        {
            ["browser"] = "Les deux",
            ["blockedUrls"] = "https://interdit.example\r\nhttps://bloque.example\r\n\r\nhttps://interdit.example",
            ["allowedUrls"] = "https://autorise.example",
        });

        var edgeBlocked = settings
            .Where(s => s.Key.EndsWith(@"\Microsoft\Edge\URLBlocklist", StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => int.Parse(s.ValueName))
            .ToList();
        var chromeBlocked = settings
            .Where(s => s.Key.EndsWith(@"\Google\Chrome\URLBlocklist", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Equal(2, edgeBlocked.Count);
        Assert.Equal(new[] { "1", "2" }, edgeBlocked.Select(s => s.ValueName));
        Assert.Equal("https://interdit.example", edgeBlocked[0].Value);
        Assert.Equal("https://bloque.example", edgeBlocked[1].Value);
        Assert.Equal(2, chromeBlocked.Count);

        var allowed = Assert.Single(settings, s => s.Key.EndsWith(@"\Microsoft\Edge\URLAllowlist", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("https://autorise.example", allowed.Value);
    }

    [Fact]
    public void ScreensaverTimeout_IsSubstitutedFromParameter()
    {
        var settings = CreateCatalog().Materialize("screensaver-lock",
            new Dictionary<string, object?> { ["timeoutSeconds"] = 900 });

        var timeout = Assert.Single(settings, s => s.ValueName == "ScreenSaveTimeOut");
        Assert.Equal("900", timeout.Value);
    }

    [Fact]
    public void AsrRules_AreStoredAsEnabledStringValues()
    {
        var settings = CreateCatalog().Materialize("asr-exploit-guard", new Dictionary<string, object?>());

        var rules = settings.Where(s => Guid.TryParse(s.ValueName, out _)).ToList();
        Assert.NotEmpty(rules);
        Assert.All(rules, rule =>
        {
            // Les règles ASR se configurent en REG_SZ « 1 », pas en REG_DWORD.
            Assert.Equal("String", rule.ValueType);
            Assert.Equal("1", rule.Value);
        });
    }

    [Fact]
    public void UnknownTemplate_MaterializationThrows()
    {
        var catalog = CreateCatalog();
        Assert.Null(catalog.GetBinding("modele-inexistant"));
        Assert.ThrowsAny<Exception>(() => catalog.Materialize("modele-inexistant", new Dictionary<string, object?>()));
    }
}
