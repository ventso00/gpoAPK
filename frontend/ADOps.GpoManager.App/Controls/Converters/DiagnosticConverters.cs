using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ADOps.GpoManager.Core.Diagnostics;

namespace ADOps.GpoManager.App.Controls.Converters;

/// <summary>Fond de pastille selon le statut d'un contrôle de diagnostic.</summary>
public sealed class DiagnosticStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var brush = value switch
        {
            DiagnosticStatus.Passed => new SolidColorBrush(Color.FromRgb(0x14, 0x3A, 0x28)),
            DiagnosticStatus.Warning => new SolidColorBrush(Color.FromRgb(0x3A, 0x2F, 0x16)),
            DiagnosticStatus.Failed => new SolidColorBrush(Color.FromRgb(0x3A, 0x26, 0x26)),
            _ => new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x33)),
        };
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Couleur de texte selon le statut d'un contrôle de diagnostic.</summary>
public sealed class DiagnosticStatusToForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var brush = value switch
        {
            DiagnosticStatus.Passed => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x7D)),
            DiagnosticStatus.Warning => new SolidColorBrush(Color.FromRgb(0xE8, 0xA3, 0x3D)),
            DiagnosticStatus.Failed => new SolidColorBrush(Color.FromRgb(0xE5, 0x53, 0x4B)),
            _ => new SolidColorBrush(Color.FromRgb(0x9B, 0x9B, 0xA6)),
        };
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Libellé court du statut (ex. : « ÉCHEC »).</summary>
public sealed class DiagnosticStatusToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            DiagnosticStatus.Passed => "OK",
            DiagnosticStatus.Warning => "Avertissement",
            DiagnosticStatus.Failed => "Échec",
            DiagnosticStatus.Skipped => "Non exécuté",
            DiagnosticStatus.Running => "En cours…",
            _ => "—",
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Libellé du niveau de risque d'une action de remédiation.</summary>
public sealed class RemediationRiskToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            RemediationRisk.Safe => "Sans risque",
            RemediationRisk.Elevated => "Modification système (admin)",
            RemediationRisk.ManualOnly => "Manuel uniquement",
            _ => "—",
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
