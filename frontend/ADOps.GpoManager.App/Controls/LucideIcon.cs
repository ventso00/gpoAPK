using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ADOps.GpoManager.App.Controls;

/// <summary>
/// Icône vectorielle du jeu Lucide (tracés 24 × 24, trait arrondi).
///
/// <para>
/// Le tracé est désigné par un <see cref="LucideIconName"/> : une faute de
/// frappe dans l'attribut XAML <c>Icon="…"</c> est rejetée à la compilation
/// plutôt que d'afficher un vide invisible. Le rendu passe par un
/// <see cref="Viewbox"/>, donc l'épaisseur de trait suit la taille demandée
/// exactement comme à l'export Lucide.
/// </para>
/// <para>
/// La propriété s'appelle <c>Icon</c> et non <c>Name</c> : en XAML, l'attribut
/// <c>Name</c> est réservé à l'enregistrement de l'élément (<c>x:Name</c>).
/// </para>
/// </summary>
public sealed class LucideIcon : Viewbox
{
    private static readonly ConcurrentDictionary<LucideIconName, Geometry?> Cache = new();

    private readonly Path _path;

    /// <summary>Identifie le tracé à afficher.</summary>
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(LucideIconName), typeof(LucideIcon),
        new PropertyMetadata(LucideIconName.Circle, OnAppearanceInvalidated));

    /// <summary>Couleur du trait.</summary>
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(LucideIcon), new PropertyMetadata(null, OnAppearanceInvalidated));

    /// <summary>Épaisseur du trait dans le repère 24 × 24 (2 = règle Lucide).</summary>
    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(LucideIcon), new PropertyMetadata(2d, OnAppearanceInvalidated));

    public LucideIcon()
    {
        Stretch = Stretch.Uniform;
        _path = new Path
        {
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Fill = Brushes.Transparent,
        };
        Child = _path;
    }

    public LucideIconName Icon
    {
        get => (LucideIconName)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>Vrai si le tracé demandé est présent et analysable dans le catalogue.</summary>
    public static bool IsAvailable(LucideIconName icon) => Resolve(icon) is not null;

    private static void OnAppearanceInvalidated(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((LucideIcon)d).Apply();

    private void Apply()
    {
        _path.Data = Resolve(Icon);

        // Sans couleur explicite, l'icône reprend la couleur du texte héritée de
        // son conteneur : dans un bouton primaire elle devient blanc sur bleu sans
        // qu'aucun style n'ait à le mentionner.
        _path.Stroke = Stroke ?? (Brush)GetValue(TextElement.ForegroundProperty);
        _path.StrokeThickness = StrokeThickness;
    }

    private static Geometry? Resolve(LucideIconName icon)
        => Cache.GetOrAdd(icon, Parse);

    private static Geometry? Parse(LucideIconName icon)
    {
        var data = LucideIconData.TryGet(icon);
        if (data is null)
        {
            return null;
        }

        try
        {
            var geometry = Geometry.Parse(data);
            geometry.Freeze();
            return geometry;
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.InvalidDataException or NotSupportedException or OverflowException)
        {
            // Un tracé illisible ne doit jamais faire tomber l'interface :
            // l'icône est absente et le trace est conservé pour le diagnostic.
            System.Diagnostics.Debug.WriteLine("Tracé Lucide « " + icon + " » invalide : " + ex.Message);
            return null;
        }
    }
}
