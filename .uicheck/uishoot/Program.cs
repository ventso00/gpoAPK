using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ADOps.GpoManager.App;
using ADOps.GpoManager.App.ViewModels;
using ADOps.GpoManager.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Uishoot;

/// <summary>
/// Outil de vérification visuelle hors écran : instancie la fenêtre réelle de
/// l'application, navigue sur chaque section et enregistre le rendu en PNG.
/// Il ne déclenche aucune écriture dans Active Directory.
/// </summary>
internal static class Program
{
    private static readonly string[] AllKeys =
    [
        "overview", "machines", "gpos", "params", "deploy", "clients", "audit", "settings",
    ];

    [STAThread]
    private static int Main(string[] args)
    {
        var outDir = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
        var scale = args.Length > 1 ? double.Parse(args[1], CultureInfo.InvariantCulture) : 1.0;
        var width = args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 1360;
        var height = args.Length > 3 ? double.Parse(args[3], CultureInfo.InvariantCulture) : 860;
        var keys = args.Length > 4 ? args[4].Split(',') : AllKeys;

        Directory.CreateDirectory(outDir);
        var app = new App();
        app.InitializeComponent();

        var provider = Bootstrapper.ConfigureServices();

        // La fenêtre récupère le conteneur via App.ServiceProvider (réglé dans
        // OnStartup) : sans cycle de vie complet, on l'alimente par réflexion.
        typeof(App)
            .GetField("_serviceProvider", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(app, provider);

        var shell = provider.GetRequiredService<MainWindow>();
        var model = provider.GetRequiredService<MainViewModel>();
        shell.DataContext = model;
        shell.WindowStartupLocation = WindowStartupLocation.Manual;
        shell.ShowInTaskbar = false;
        shell.Top = 40;
        shell.Left = 40;
        shell.Width = width;
        shell.Height = height;
        shell.LayoutTransform = new ScaleTransform(scale, scale);
        shell.Show();
        Drain(shell);

        // Le cache SQLite local alimente les listes : sans ce rechargement, les
        // tableaux restent vides et ne peuvent pas être validés visuellement.
        model.Workspace.ReloadWorkspaceCommand.Execute(null);
        for (var i = 0; i < 60 && model.Workspace.IsBusy; i++)
        {
            Drain(shell);
            Thread.Sleep(200);
        }
        Drain(shell);

        foreach (var key in keys)
        {
            model.NavigateToCommand.Execute(key);
            Drain(shell);
            var file = Path.Combine(outDir, $"{key}_x{scale:0.##}_{width:0}x{height:0}.png");
            Capture(shell, file, scale);
            Console.WriteLine("ecrit " + file);

            var scroller = FindScroll(shell);
            if (scroller is { ScrollableHeight: > 0 })
            {
                scroller.ScrollToVerticalOffset(scroller.ScrollableHeight);
                Drain(shell);
                var low = Path.Combine(outDir, $"{key}_bas_x{scale:0.##}.png");
                Capture(shell, low, scale);
                Console.WriteLine("ecrit " + low);
            }

            if (key == "clients")
            {
                TryDropDown(shell, outDir);
            }
        }

        shell.Close();
        return 0;
    }

    /// <summary>
    /// Ouvre la liste « Déplacer vers l'OU » et rend le contenu du Popup : le gabarit
    /// Fluent du ComboBox est nouveau, sa partie déroulante doit donc être vérifiée.
    /// </summary>
    private static void TryDropDown(Window shell, string outDir)
    {
        ComboBox? combo = null;
        try
        {
            Walk(shell);
            if (combo is null)
            {
                Console.WriteLine("combo introuvable");
                return;
            }

            combo.IsDropDownOpen = true;
            Drain(shell);
            Thread.Sleep(300);
            Drain(shell);

            if (combo.Template.FindName("Popup", combo) is Popup { Child: FrameworkElement child })
            {
                child.UpdateLayout();
                if (child.RenderSize.Width > 1 && child.RenderSize.Height > 1)
                {
                    var file = Path.Combine(outDir, "combo_ouvert.png");
                    Capture(child, file, 1.0);
                    Console.WriteLine("ecrit " + file);
                }
                else
                {
                    Console.WriteLine("popup non rendu (hors ecran)");
                }
            }

            combo.IsDropDownOpen = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine("dropdown: " + ex.GetType().Name + " " + ex.Message);
        }

        void Walk(DependencyObject node)
        {
            var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(node, i);
                if (child is ComboBox box && combo is null)
                {
                    combo = box;
                }
                Walk(child);
            }
        }
    }

    private static ScrollViewer? FindScroll(DependencyObject root)
    {
        ScrollViewer? best = null;
        Walk(root);
        return best;

        void Walk(DependencyObject node)
        {
            var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(node, i);
                if (child is ScrollViewer viewer
                    && (best is null || viewer.ScrollableHeight > best.ScrollableHeight))
                {
                    best = viewer;
                }
                Walk(child);
            }
        }
    }

    private static void Drain(Window shell)
    {
        for (var i = 0; i < 4; i++)
        {
            shell.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
            shell.UpdateLayout();
        }
    }

    private static void Capture(FrameworkElement visual, string file, double scale)
    {
        var box = new Rect(visual.RenderSize);
        var pixels = new System.Windows.Int32Rect(
            0,
            0,
            Math.Max(1, (int)Math.Ceiling(box.Width * scale)),
            Math.Max(1, (int)Math.Ceiling(box.Height * scale)));
        var bitmap = new RenderTargetBitmap(pixels.Width, pixels.Height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file);
        encoder.Save(stream);
    }
}
