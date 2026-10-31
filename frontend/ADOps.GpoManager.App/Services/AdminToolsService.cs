using System.Diagnostics;
using System.IO;

namespace ADOps.GpoManager.App.Services;

/// <summary>
/// Outil d'administration Windows proposé à l'utilisateur : une console MMC
/// (Active Directory, gestion de l'ordinateur, stratégies de groupe…) et les
/// chemins où la trouver sur la machine courante.
/// </summary>
public sealed record AdminToolDescriptor(
    string Id,
    string DisplayName,
    string Description,
    string FileName,
    string ArgumentsTemplate);

/// <summary>
/// Intégration des outils d'administration Windows présents sur la machine.
/// Le dossier « Administrative Tools » est localisé : son nom est traduit
/// selon la langue de Windows (« Administrative Tools », « Outils
/// d'administration »…). Les candidats sont donc tous testés, puis le dossier
/// de programmes est balayé en dernier recours.
/// </summary>
public sealed class AdminToolsService
{
    private static readonly string[] AdministrativeToolsFolderNames =
    {
        "Administrative Tools",
        "Outils d'administration",
        "Outils d’administration", // apostrophe typographique (Windows en français)
        "Administrative Tools (Admin)",
    };

    private static readonly AdminToolDescriptor[] KnownTools =
    {
        new(
            "aduc",
            "Utilisateurs et ordinateurs Active Directory",
            "Console ADUC : comptes, ordinateurs et OU du domaine.",
            "dsa.msc",
            "/dc={dc}"),
        new(
            "ads",
            "Ordinateurs et services Active Directory",
            "Sites, sous-réseaux, trusts et réplication.",
            "dsr.msc",
            "/dc={dc}"),
        new(
            "gpedit",
            "Gestion des stratégies de groupe",
            "Éditeur local de stratégies de groupe de la machine.",
            "gpedit.msc",
            string.Empty),
        new(
            "compmgmt",
            "Gestion de l'ordinateur",
            "Services, stockage, gestion des périphériques.",
            "compmgmt.msc",
            string.Empty),
        new(
            "services",
            "Services",
            "Services Windows locaux.",
            "services.msc",
            string.Empty),
        new(
            "localusers",
            "Utilisateurs et computers locaux",
            "Comptes locaux de la machine.",
            "lusrmgr.msc",
            string.Empty),
        new(
            "quotas",
            "Gestion des quotas de volumes",
            "Quotas de disque par volume.",
            "disksnap.msc",
            string.Empty),
    };

    /// <summary>Dossier de programmes du menu Démarrer (ancêtre du dossier des outils).</summary>
    public string ProgramsFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
        "Microsoft",
        "Windows",
        "Start Menu",
        "Programs");

    /// <summary>
    /// Dossier « Outils d'administration » réellement présent sur cette machine
    /// (null s'il est introuvable). Le nom du dossier dépend de la langue de
    /// Windows : les variantes connues sont testées, puis un balayage du
    /// dossier parent permet de retrouver un nom traduit inconnu.
    /// </summary>
    public string? AdministrativeToolsFolder { get; } = ResolveAdministrativeToolsFolder();

    /// <summary>Tous les outils connus, avec leur disponibilité sur cette machine.</summary>
    public IReadOnlyList<AdminToolInfo> GetTools()
    {
        var tools = new List<AdminToolInfo>();
        foreach (var descriptor in KnownTools)
        {
            var path = ResolveToolPath(descriptor);
            tools.Add(new AdminToolInfo(descriptor, path is not null, path));
        }

        return tools;
    }

    /// <summary>
    /// Raccourcis réellement installés dans le dossier « Outils d'administration »
    /// (fichiers .lnk et .msc). Aucune résolution de cible n'est nécessaire :
    /// le shell de Windows les exécute directement.
    /// </summary>
    public IReadOnlyList<string> GetInstalledShortcuts()
    {
        var folder = AdministrativeToolsFolder;
        if (folder is null || !Directory.Exists(folder))
        {
            return Array.Empty<string>();
        }

        try
        {
            return Directory
                .GetFiles(folder)
                .Where(path =>
                {
                    var extension = Path.GetExtension(path);
                    return string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(extension, ".msc", StringComparison.OrdinalIgnoreCase);
                })
                .Select(path => Path.GetFileNameWithoutExtension(path))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Lance un outil d'administration. Les consoles MMC d'Active Directory sont
    /// des snap-ins 32 bits : <c>System32</c> est cherché en premier, puis
    /// <c>SysWOW64</c>.
    /// </summary>
    public bool Launch(AdminToolDescriptor tool, string? domainControllerFqdn, out string error)
    {
        error = string.Empty;

        var path = ResolveToolPath(tool);
        if (path is null)
        {
            error = "L'outil « " + tool.DisplayName + " » n'est pas installé sur cette machine.";
            return false;
        }

        var arguments = string.IsNullOrEmpty(tool.ArgumentsTemplate)
            ? string.Empty
            : tool.ArgumentsTemplate.Replace(
                "{dc}",
                string.IsNullOrWhiteSpace(domainControllerFqdn) ? string.Empty : domainControllerFqdn.Trim(),
                StringComparison.OrdinalIgnoreCase);

        try
        {
            Process.Start(new ProcessStartInfo(path, arguments) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            error = "Impossible de lancer « " + tool.DisplayName + " » : " + ex.Message;
            return false;
        }
    }

    /// <summary>Ouvre un raccourci du dossier « Outils d'administration ».</summary>
    public bool LaunchShortcut(string shortcutPath, out string error)
    {
        error = string.Empty;
        if (!File.Exists(shortcutPath))
        {
            error = "Le raccourci est introuvable : " + shortcutPath;
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(shortcutPath) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            error = "Impossible d'ouvrir le raccourci : " + ex.Message;
            return false;
        }
    }

    /// <summary>Ouvre le dossier « Outils d'administration » dans l'explorateur.</summary>
    public bool OpenAdministrativeToolsFolder(out string error)
    {
        error = string.Empty;
        var folder = AdministrativeToolsFolder;
        if (folder is null || !Directory.Exists(folder))
        {
            error = "Le dossier « Outils d'administration » est introuvable sur cette machine.";
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            error = "Impossible d'ouvrir le dossier : " + ex.Message;
            return false;
        }
    }

    /// <summary>Chemin de l'exécutable d'un outil, ou null s'il est absent.</summary>
    public string? ResolveToolPath(AdminToolDescriptor tool)
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var windows = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";

        var candidates = new[]
        {
            Path.Combine(system, tool.FileName),
            Path.Combine(windows, "System32", tool.FileName),
            Path.Combine(windows, "SysWOW64", tool.FileName),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? ResolveAdministrativeToolsFolder()
    {
        var programs = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            "Microsoft",
            "Windows",
            "Start Menu",
            "Programs");

        // 1. Noms connus (anglais et français).
        foreach (var name in AdministrativeToolsFolderNames)
        {
            var candidate = Path.Combine(programs, name);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        // 2. Repli : recherche d'un sous-dossier dont le nom évoque l'administration.
        try
        {
            if (!Directory.Exists(programs))
            {
                return null;
            }

            foreach (var directory in Directory.GetDirectories(programs))
            {
                var name = Path.GetFileName(directory);
                if (name.Contains("admin", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Outils d", StringComparison.OrdinalIgnoreCase))
                {
                    return directory;
                }
            }
        }
        catch (Exception)
        {
            return null;
        }

        return null;
    }
}

/// <summary>Outil d'administration et sa disponibilité sur la machine courante.</summary>
public sealed record AdminToolInfo(AdminToolDescriptor Descriptor, bool IsInstalled, string? Path);
