using System.Globalization;
using System.Windows.Data;
using ADOps.GpoManager.Core.Models;

namespace ADOps.GpoManager.App.Controls.Converters;

/// <summary>Convertit le statut de support d'un modèle en libellé.</summary>
public sealed class TemplateSupportToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is TemplateSupportStatus status ? status switch
        {
            TemplateSupportStatus.Available => "Disponible",
            TemplateSupportStatus.MissingPrerequisite => "Prérequis manquant",
            TemplateSupportStatus.NotSupported => "Non pris en charge",
            TemplateSupportStatus.ComingSoon => "À venir",
            _ => "Inconnu",
        } : "Inconnu";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
