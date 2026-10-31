using System.IO;
using System.Text.RegularExpressions;
using ADOps.GpoManager.App.Controls;
using System.Windows.Media;
using Xunit;

namespace ADOps.GpoManager.Tests;

/// <summary>
/// Le catalogue d'icônes Lucide est la seule source des tracés de l'interface :
/// un tracé illisible ou une référence inventée dans un XAML se traduirait par
/// une icône manquante, donc par une navigation illisible. Ces tests valident
/// le catalogue et son usage réel dans les vues.
/// </summary>
public class LucideIconCatalogTests
{
    private static readonly Regex IconAttribute = new Regex(
        "Icon=\"(?<name>[A-Za-z]+)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void Catalogue_ChaqueIconeDeclareeEstAnalyseEtDansLeRepere24()
    {
        var problems = new List<string>();

        foreach (var icon in Enum.GetValues<LucideIconName>())
        {
            var data = LucideIconData.TryGet(icon);
            if (data is null)
            {
                problems.Add(icon + " : aucun tracé dans le catalogue");
                continue;
            }

            try
            {
                var geometry = Geometry.Parse(data);
                var bounds = geometry.Bounds;

                // Les tracés sont dessinés dans un repère 24 × 24 : hors de ce
                // cadre, une valeur mal retranscuite (virgule, signe) fausse la
                // mise en page de tout ce qui entoure l'icône.
                if (bounds.Width <= 0 || bounds.Height <= 0)
                {
                    problems.Add(icon + " : tracé vide");
                }
                else if (bounds.Right > 24.5 || bounds.Bottom > 24.5 || bounds.Left < -0.5 || bounds.Top < -0.5)
                {
                    problems.Add(icon + " : hors du repère 24 × 24 (" + bounds + ")");
                }
            }
            catch (Exception ex)
            {
                problems.Add(icon + " : " + ex.GetType().Name + " — " + ex.Message);
            }
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void Catalogue_LesIconesDeNavigationSontResolues()
    {
        foreach (var icon in new[]
                 {
                     LucideIconName.LayoutDashboard, LucideIconName.Monitor, LucideIconName.ListChecks,
                     LucideIconName.SlidersHorizontal, LucideIconName.Rocket, LucideIconName.Terminal,
                     LucideIconName.History, LucideIconName.Settings, LucideIconName.Search,
                     LucideIconName.RefreshCw, LucideIconName.Lock, LucideIconName.ShieldCheck,
                 })
        {
            Assert.True(LucideIcon.IsAvailable(icon), "Tracé indisponible : " + icon);
        }
    }

    [Fact]
    public void Xaml_DesVues_Vivent_NUtiliseQueDesIconesConnues()
    {
        var appDirectory = FindAppDirectory();
        var known = Enum.GetNames<LucideIconName>().ToHashSet(StringComparer.Ordinal);
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(appDirectory, "*.xaml", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            if (!text.Contains("LucideIcon", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in IconAttribute.Matches(text))
            {
                var name = match.Groups["name"].Value;
                if (!known.Contains(name))
                {
                    offenders.Add(Path.GetFileName(file) + " → Icon=\"" + name + "\"");
                }
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// Racine du projet WPF, trouvée en remontant depuis le dossier de test
    /// jusqu'au fichier solution (le test s'exécute depuis bin/...).
    /// </summary>
    private static string FindAppDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "frontend", "ADOps.GpoManager.App");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Dossier frontend/ADOps.GpoManager.App introuvable depuis " + AppContext.BaseDirectory);
    }
}
