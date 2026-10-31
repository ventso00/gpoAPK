namespace ADOps.GpoManager.App.Controls;

/// <summary>
/// Noms des icônes disponibles. Le type est un <c>enum</c> volontairement :
/// les attributs XAML <c>Icon="…"</c> sont ainsi validés à la compilation.
/// </summary>
public enum LucideIconName
{
    Circle,
    LayoutDashboard,
    Monitor,
    Users,
    Server,
    ListChecks,
    SlidersHorizontal,
    Rocket,
    Terminal,
    History,
    Settings,
    Search,
    RefreshCw,
    Lock,
    ShieldCheck,
    Close,
    Check,
    Plus,
    ChevronRight,
    ChevronLeft,
    ChevronDown,
    ArrowRight,
    ArrowRightLeft,
    Power,
    RotateCcw,
    TriangleAlert,
    Info,
    CircleCheck,
    CircleX,
    CircleAlert,
    Ban,
    Eye,
    Clock,
    Activity,
    Database,
    FileText,
    Play,
    Stop,
    Filter,
    LogOut,
    CircleUser,
    ExternalLink,
    Wifi,
    BarChart,
    Folder,
    KeyRound,
    Notebook,
}

/// <summary>
/// Catalogue des tracés Lucide (licence CC0) au repère 24 × 24, trait 2,
/// extrémités et jonctions arrondies. Les données sont exprimées en commandes
/// du mini-langage de chemin WPF, avec un séparateur explicite entre chaque
/// nombre : rien n'est laissé à l'interprétation du parseur.
/// </summary>
public static class LucideIconData
{
    /// <summary>Retourne le tracé demandé, ou <c>null</c> s'il n'existe pas.</summary>
    public static string? TryGet(LucideIconName name) => name switch
    {
        LucideIconName.Circle => "M12 5 a7 7 0 1 0 0 14 a7 7 0 1 0 0 -14 z",
        LucideIconName.LayoutDashboard => "M3 3 H10 V12 H3 Z M14 3 H21 V8 H14 Z M14 12 H21 V21 H14 Z M3 16 H10 V21 H3 Z",
        LucideIconName.Monitor => "M2 5 a2 2 0 0 1 2 -2 H20 a2 2 0 0 1 2 2 V15 a2 2 0 0 1 -2 2 H4 a2 2 0 0 1 -2 -2 Z M8 21 H16 M12 17 V21",
        LucideIconName.Users => "M16 21 V19 a4 4 0 0 0 -4 -4 H6 a4 4 0 0 0 -4 4 V21 M5 7 a4 4 0 1 0 8 0 a4 4 0 1 0 -8 0 M22 21 V19 a4 4 0 0 0 -3 -3.87 M16 3.13 a4 4 0 0 1 0 7.75",
        LucideIconName.Server => "M5 4 a2 2 0 0 0 -2 2 V9 a2 2 0 0 0 2 2 H19 a2 2 0 0 0 2 -2 V6 a2 2 0 0 0 -2 -2 Z M5 13 a2 2 0 0 0 -2 2 V18 a2 2 0 0 0 2 2 H19 a2 2 0 0 0 2 -2 V15 a2 2 0 0 0 -2 -2 Z M7 7 H7.01 M7 16 H7.01",
        LucideIconName.ListChecks => "M3 17 L5 19 L9 15 M3 7 L5 9 L9 5 M13 18 H21 M13 13 H21 M13 8 H21",
        LucideIconName.SlidersHorizontal => "M21 4 H14 M10 4 H3 M21 12 H12 M8 12 H3 M21 20 H16 M12 20 H3 M14 2 V6 M8 10 V14 M16 18 V22",
        LucideIconName.Rocket => "M4.5 16.5 c-1.5 1.26 -2 5 -2 5 s3.74 -0.5 5 -2 c0.71 -0.84 0.7 -2.13 -0.09 -2.91 a2.18 2.18 0 0 0 -2.91 -0.09 z M12 15 l-3 -3 a22 22 0 0 1 2 -3.95 A12.88 12.88 0 0 1 22 2 c0 2.72 -0.78 7.5 -6 11 a22.35 22.35 0 0 1 -4 2 z M9 12 H4 s0.55 -3.03 2 -4 c1.62 -1.08 5 0 5 0 M12 15 v5 s3.03 -0.55 4 -2 c1.08 -1.62 0 -5 0 -5",
        LucideIconName.Terminal => "M4 5 a2 2 0 0 1 2 -2 h12 a2 2 0 0 1 2 2 v14 a2 2 0 0 1 -2 2 H6 a2 2 0 0 1 -2 -2 z M7 9 l2 2 -2 2 M12 15 h4",
        LucideIconName.History => "M3 12 a9 9 0 1 0 9 -9 a9.75 9.75 0 0 0 -6.74 2.74 L3 8 M3 3 v5 h5 M12 7 v5 l4 2",
        LucideIconName.Settings => "M12 6 a6 6 0 1 0 0 12 a6 6 0 1 0 0 -12 z M12 9.5 a2.5 2.5 0 1 0 0 5 a2.5 2.5 0 1 0 0 -5 z M18.5 12 H21 M16.6 7.4 L18.4 5.6 M12 5.5 V3 M7.4 7.4 L5.6 5.6 M5.5 12 H3 M7.4 16.6 L5.6 18.4 M12 18.5 V21 M16.6 16.6 L18.4 18.4",
        LucideIconName.Search => "M4 11 a7 7 0 1 0 14 0 a7 7 0 1 0 -14 0 M21 21 L16.3 16.3",
        LucideIconName.RefreshCw => "M3 12 a9 9 0 0 1 9 -9 a9.75 9.75 0 0 1 6.74 2.74 L21 8 M21 3 v5 h-5 M21 12 a9 9 0 0 1 -9 9 a9.75 9.75 0 0 1 -6.74 -2.74 L3 16 M8 16 H3 v5",
        LucideIconName.Lock => "M5 11 h14 a1 1 0 0 1 1 1 v7 a1 1 0 0 1 -1 1 H5 a1 1 0 0 1 -1 -1 v-7 a1 1 0 0 1 1 -1 z M8 11 V7 a4 4 0 0 1 8 0 v4",
        LucideIconName.ShieldCheck => "M20 13 c0 5 -3.5 7.5 -7.66 8.95 a1 1 0 0 1 -0.67 0 C7.5 20.5 4 18 4 13 V6 a1 1 0 0 1 1 -1 c2 0 4.5 -1.2 6.24 -2.72 a1.17 1.17 0 0 1 1.52 0 C14.51 3.81 17 5 19 5 a1 1 0 0 1 1 1 z M9 12 l2 2 4 -4",
        LucideIconName.Close => "M18 6 L6 18 M6 6 l12 12",
        LucideIconName.Check => "M20 6 L9 17 l-5 -5",
        LucideIconName.Plus => "M12 5 v14 M5 12 h14",
        LucideIconName.ChevronRight => "M9 18 l6 -6 -6 -6",
        LucideIconName.ChevronLeft => "M15 18 l-6 -6 6 -6",
        LucideIconName.ChevronDown => "M6 9 l6 6 6 -6",
        LucideIconName.ArrowRight => "M5 12 h14 M13 6 l6 6 -6 6",
        LucideIconName.ArrowRightLeft => "M7.5 3 l4.5 4.5 -4.5 4.5 M12 7.5 H21 M16.5 12 l-4.5 4.5 4.5 4.5 M12 16.5 H3",
        LucideIconName.Power => "M12 2 v10 M18.4 6.6 a9 9 0 1 1 -12.77 0.04",
        LucideIconName.RotateCcw => "M3 12 a9 9 0 1 0 9 -9 a9.75 9.75 0 0 0 -6.74 2.74 L3 8 M3 3 v5 h5",
        LucideIconName.TriangleAlert => "M10.29 3.86 L1.82 18 a2 2 0 0 0 1.71 3 h16.94 a2 2 0 0 0 1.71 -3 L13.71 3.86 a2 2 0 0 0 -3.42 0 z M12 9 v4 M12 17 H12.01",
        LucideIconName.Info => "M12 2 a10 10 0 1 0 0 20 a10 10 0 1 0 0 -20 z M12 16 v-4 M12 8 H12.01",
        LucideIconName.CircleCheck => "M12 3 a9 9 0 1 0 0 18 a9 9 0 1 0 0 -18 z M8.5 12 l2.5 2.5 4.5 -4.5",
        LucideIconName.CircleX => "M12 3 a9 9 0 1 0 0 18 a9 9 0 1 0 0 -18 z M15 9 l-6 6 M9 9 l6 6",
        LucideIconName.CircleAlert => "M12 3 a9 9 0 1 0 0 18 a9 9 0 1 0 0 -18 z M12 8 v4 M12 16 H12.01",
        LucideIconName.Ban => "M12 3 a9 9 0 1 0 0 18 a9 9 0 1 0 0 -18 z M5.6 5.6 l12.8 12.8",
        LucideIconName.Eye => "M2 12 c0 0 3.5 -7 10 -7 s10 7 10 7 c0 0 -3.5 7 -10 7 S2 12 2 12 z M12 9 a3 3 0 1 0 0 6 a3 3 0 1 0 0 -6 z",
        LucideIconName.Clock => "M12 2 a10 10 0 1 0 0 20 a10 10 0 1 0 0 -20 z M12 6 v6 l4 2",
        LucideIconName.Activity => "M22 12 h-4 l-3 9 L9 3 l-3 9 H2",
        LucideIconName.Database => "M3 5 a9 3 0 1 0 18 0 a9 3 0 1 0 -18 0 z M3 5 v14 a9 3 0 0 0 18 0 V5 M3 12 a9 3 0 0 0 18 0",
        LucideIconName.FileText => "M14 2 H6 a2 2 0 0 0 -2 2 v16 a2 2 0 0 0 2 2 h12 a2 2 0 0 0 2 -2 V7 z M14 2 v5 h5 M16 13 H8 M16 17 H8 M10 9 H8",
        LucideIconName.Play => "M7 4 v16 l13 -8 z",
        LucideIconName.Stop => "M6 5 h12 a1 1 0 0 1 1 1 v12 a1 1 0 0 1 -1 1 H6 a1 1 0 0 1 -1 -1 V6 a1 1 0 0 1 1 -1 z",
        LucideIconName.Filter => "M22 3 H2 l8 9.46 V19 l4 2 V12.46 z",
        LucideIconName.LogOut => "M9 21 H5 a2 2 0 0 1 -2 -2 V5 a2 2 0 0 1 2 -2 h4 M16 17 l5 -5 -5 -5 M21 12 H9",
        LucideIconName.CircleUser => "M12 2 a10 10 0 1 0 0 20 a10 10 0 1 0 0 -20 z M12 6 a3 3 0 1 0 0 6 a3 3 0 1 0 0 -6 z M7 18 a5.5 5.5 0 0 1 10 0",
        LucideIconName.ExternalLink => "M15 3 h6 v6 M10 14 L21 3 M18 13 v6 a2 2 0 0 1 -2 2 H5 a2 2 0 0 1 -2 -2 V8 a2 2 0 0 1 2 -2 h6",
        LucideIconName.Wifi => "M5 13 a10 10 0 0 1 14 0 M8.5 16.5 a5 5 0 0 1 7 0 M2 8.82 a15 15 0 0 1 20 0 M12 20 H12.01",
        LucideIconName.BarChart => "M3 3 v18 h18 M8 17 v-6 M13 17 V7 M18 17 v-3",
        LucideIconName.Folder => "M4 5 a2 2 0 0 1 2 -2 h4 l2 3 h8 a2 2 0 0 1 2 2 v9 a2 2 0 0 1 -2 2 H4 a2 2 0 0 1 -2 -2 V5 z",
        LucideIconName.KeyRound => "M12 10 a2 2 0 1 0 0 4 a2 2 0 1 0 0 -4 z M12 10 L18.5 3.5 M15.5 6.5 L18 9 M12 2 a10 10 0 1 0 0 20 a10 10 0 1 0 0 -20 z",
        LucideIconName.Notebook => "M6 3 a2 2 0 0 0 -2 2 v14 a2 2 0 0 0 2 2 h13 V3 z M4 8 h2 M4 12 h2 M4 16 h2 M9 8 h6 M9 12 h6",

        // Nom sans tracé connu : rien n'est affiché plutôt qu'un symbole trompeur.
        _ => null,
    };

    /// <summary>Tous les noms déclarés dans le catalogue (contrôle de cohérence).</summary>
    public static IEnumerable<LucideIconName> AllNames { get; } =
        Enum.GetValues<LucideIconName>().Where(name => TryGet(name) is not null);
}
