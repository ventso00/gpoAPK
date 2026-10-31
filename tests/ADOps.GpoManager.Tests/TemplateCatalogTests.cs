using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Templates;
using Xunit;

namespace ADOps.GpoManager.Tests;

/// <summary>
/// Invariants du cadre d'extension du catalogue : aucun modèle embarqué ne
/// doit être rejeté silencieusement, les identifiants sont uniques, les
/// métadonnées sont complètes et la validation des paramètres est générique.
/// </summary>
public class TemplateCatalogTests
{
    private static readonly string[] RiskLevels = ["Low", "Medium", "High"];

    private static TemplateCatalog CreateCatalog() => new();

    [Fact]
    public void AllEmbeddedJsonTemplates_AreLoaded()
    {
        var catalog = CreateCatalog();
        // Le catalogue consolidé gpo_catalog.json (scripts PowerShell réels) est
        // embarqué dans la même assembly mais n'est PAS un modèle de template :
        // on l'exclut du décompte.
        var jsonResources = typeof(TemplateCatalog).Assembly
            .GetManifestResourceNames()
            .Count(name => name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                           && !name.EndsWith("gpo_catalog.json", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(jsonResources, catalog.GetTemplates().Count);
    }

    [Fact]
    public void TemplateIds_AreUnique_AndMatchFileName()
    {
        var catalog = CreateCatalog();
        var templates = catalog.GetTemplates();

        var distinctIds = templates
            .Select(template => template.TemplateId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Assert.Equal(templates.Count, distinctIds.Count);

        foreach (var template in templates)
        {
            var matchingResource = typeof(TemplateCatalog).Assembly
                .GetManifestResourceNames()
                .Any(name => name.EndsWith(template.TemplateId + ".json", StringComparison.OrdinalIgnoreCase));
            Assert.True(matchingResource, "templateId sans fichier correspondant : " + template.TemplateId);
        }
    }

    [Fact]
    public void AllTemplates_HaveCompleteMetadata()
    {
        foreach (var template in CreateCatalog().GetTemplates())
        {
            Assert.Equal(TemplateCatalog.CurrentSchemaVersion, template.SchemaVersion);
            Assert.False(string.IsNullOrWhiteSpace(template.DisplayName), template.TemplateId);
            Assert.False(string.IsNullOrWhiteSpace(template.Category), template.TemplateId);
            Assert.False(string.IsNullOrWhiteSpace(template.Description), template.TemplateId);
            Assert.False(string.IsNullOrWhiteSpace(template.ExpectedEffect), template.TemplateId);
            Assert.True(Enum.IsDefined(template.Scope), template.TemplateId);
            Assert.Contains(template.RiskLevel, RiskLevels);
            Assert.NotEmpty(template.SupportedClients);

            if (template.SupportStatus == TemplateSupportStatus.Available)
            {
                Assert.False(string.IsNullOrWhiteSpace(template.SupportNote),
                    "Un modèle disponible doit prouver son chemin de stratégie : " + template.TemplateId);
            }
        }
    }

    [Fact]
    public void EnumParameters_DeclareAllowedValues()
    {
        foreach (var template in CreateCatalog().GetTemplates())
        {
            foreach (var parameter in template.Parameters)
            {
                Assert.False(string.IsNullOrWhiteSpace(parameter.Name), template.TemplateId);
                Assert.False(string.IsNullOrWhiteSpace(parameter.DisplayName), template.TemplateId);
                Assert.True(Enum.IsDefined(parameter.Type), template.TemplateId + "/" + parameter.Name);

                if (parameter.Type == TemplateParameterType.Enum)
                {
                    Assert.NotNull(parameter.EnumValues);
                    Assert.NotEmpty(parameter.EnumValues);
                }
            }
        }
    }

    [Fact]
    public void GetTemplates_AreSortedByCategoryThenDisplayName()
    {
        var templates = CreateCatalog().GetTemplates();

        for (var i = 1; i < templates.Count; i++)
        {
            var previous = templates[i - 1];
            var current = templates[i];
            var byCategory = string.Compare(previous.Category, current.Category, StringComparison.OrdinalIgnoreCase);
            Assert.True(byCategory < 0
                || (byCategory == 0 && string.Compare(previous.DisplayName, current.DisplayName, StringComparison.OrdinalIgnoreCase) <= 0),
                "Ordre de tri attendu : catégorie puis nom d'affichage.");
        }
    }

    [Fact]
    public void MandatoryRule_ControlPanelTemplate_IsAvailableAndUserScoped()
    {
        var catalog = CreateCatalog();

        var template = catalog.GetById("control-panel-settings");

        Assert.NotNull(template);
        Assert.Equal(TemplateSupportStatus.Available, template!.SupportStatus);
        Assert.Equal(PolicyScope.User, template.Scope);
        Assert.Contains("NoControlPanel", template.SupportNote, StringComparison.OrdinalIgnoreCase);

        var parameter = Assert.Single(template.Parameters);
        Assert.Equal("blockControlPanel", parameter.Name);
        Assert.Equal(TemplateParameterType.Boolean, parameter.Type);
        Assert.True(parameter.IsRequired);
    }

    [Fact]
    public void ValidateParameters_AcceptsValidBooleanValue()
    {
        var result = CreateCatalog().ValidateParameters("control-panel-settings",
            new Dictionary<string, object?> { ["blockControlPanel"] = "true" });

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void ValidateParameters_RequiredParameter_IsSatisfiedByDocumentedDefault()
    {
        // Chaque modèle livré possède des valeurs par défaut exécutables :
        // l'absence de paramètre ne doit jamais produire une GPO vide.
        var result = CreateCatalog().ValidateParameters("control-panel-settings", new Dictionary<string, object?>());

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void ValidateParameters_WarnsOnUnknownParameter()
    {
        var result = CreateCatalog().ValidateParameters("control-panel-settings",
            new Dictionary<string, object?> { ["blockControlPanel"] = "true", ["inconnu"] = "x" });

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, warning => warning.Contains("inconnu", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateParameters_RejectsInvalidEnumValue()
    {
        var result = CreateCatalog().ValidateParameters("powershell-execution-policy",
            new Dictionary<string, object?> { ["executionPolicy"] = "Bogus" });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Bogus", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateParameters_IntegerParameter_ChecksNumericValue()
    {
        var catalog = CreateCatalog();

        var valid = catalog.ValidateParameters("screensaver-lock",
            new Dictionary<string, object?> { ["timeoutSeconds"] = "600" });
        Assert.True(valid.IsValid, string.Join(" | ", valid.Errors));

        var invalid = catalog.ValidateParameters("screensaver-lock",
            new Dictionary<string, object?> { ["timeoutSeconds"] = "abc" });
        Assert.False(invalid.IsValid);
    }

    [Fact]
    public void ValidateParameters_UnknownTemplate_ReturnsError()
    {
        var result = CreateCatalog().ValidateParameters("inexistant", new Dictionary<string, object?>());

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    /// <summary>
    /// Scénario métier : restriction du Panneau de configuration / Paramètres
    /// pour les utilisateurs standards (NoControlPanel, portée Utilisateur),
    /// les administrateurs conservant l'accès (GPO liée à l'OU des utilisateurs
    /// standards uniquement, ou filtrage de sécurité).
    /// </summary>
    [Fact]
    public void ControlPanelRestriction_Template_IsPresent_AndConfiguredForStandardUsers()
    {
        var catalog = CreateCatalog();
        var template = catalog.GetById("control-panel-settings");
        Assert.NotNull(template);

        Assert.Equal("Restrictions", template!.Category);
        Assert.Equal(PolicyScope.User, template.Scope);
        Assert.Equal(TemplateSupportStatus.Available, template.SupportStatus);
        Assert.Contains("NoControlPanel", template.SupportNote, StringComparison.Ordinal);

        var param = Assert.Single(template.Parameters);
        Assert.Equal("blockControlPanel", param.Name);
        Assert.Equal(TemplateParameterType.Boolean, param.Type);
        Assert.True(param.IsRequired);

        // Validation : paramètre fourni => OK ; absent => valeurs par défaut du modèle.
        var ok = catalog.ValidateParameters("control-panel-settings",
            new Dictionary<string, object?> { ["blockControlPanel"] = true });
        Assert.True(ok.IsValid, string.Join(" | ", ok.Errors));

        var defaulted = catalog.ValidateParameters("control-panel-settings", new Dictionary<string, object?>());
        Assert.True(defaulted.IsValid, string.Join(" | ", defaulted.Errors));

        // Matérialisation : la valeur par défaut active bien NoControlPanel.
        var settings = catalog.Materialize("control-panel-settings", new Dictionary<string, object?>());
        Assert.Contains(settings, setting => setting.ValueName == "NoControlPanel" && setting.Value == "1");
    }
}
