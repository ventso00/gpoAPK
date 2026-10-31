using System.Globalization;
using System.Windows.Data;

namespace ADOps.GpoManager.App.Controls.Converters;

/// <summary>Convertit un booléen en « Oui » / « Non » pour l'affichage.</summary>
public sealed class BooleanToYesNoConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool boolean && boolean ? "Oui" : "Non";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}