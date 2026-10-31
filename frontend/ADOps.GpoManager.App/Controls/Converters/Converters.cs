using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ADOps.GpoManager.App.Controls.Converters;

/// <summary>Inverse la conversion booléen -> visibilité.</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Affiche un élément uniquement si la valeur n'est pas null (ex. : texte de résolution).</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Inverse une valeur booléenne (bool -> bool).</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => !(value is true);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Map un booléen vers le pinceau Succès (vert) ou Danger (rouge) du thème.</summary>
public sealed class BooleanToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var success = value is true;
        return Application.Current.TryFindResource(success ? "SuccessBrush" : "DangerBrush") is Brush brush
            ? brush
            : success ? Brushes.Green : Brushes.Red;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Masque un élément dont la chaîne est nulle ou vide.</summary>
public sealed class EmptyStringToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Pastille de statut d'une étape du health check, palette mode clair
/// (vert #10B981, ambre #F59E0B, rouge #DC2626, gris #94A3B8).
/// </summary>
public sealed class CheckStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var color = value switch
        {
            ADOps.GpoManager.Core.Models.CheckStatus.Passed => Color.FromRgb(0x10, 0xB9, 0x81),
            ADOps.GpoManager.Core.Models.CheckStatus.Warning => Color.FromRgb(0xF5, 0x9E, 0x0B),
            ADOps.GpoManager.Core.Models.CheckStatus.Failed => Color.FromRgb(0xDC, 0x26, 0x26),
            _ => Color.FromRgb(0x94, 0xA3, 0xB8),
        };
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
