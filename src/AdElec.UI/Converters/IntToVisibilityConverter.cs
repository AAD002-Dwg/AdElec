using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AdElec.UI.Converters;

/// <summary>
/// Convierte int → Visibility: valor > 0 = Visible, 0 = Collapsed.
/// Usado para mostrar/ocultar el warning de SIN_CLASIFICAR en AutoAmbientesDialog.
/// </summary>
[ValueConversion(typeof(int), typeof(Visibility))]
public sealed class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int n && n > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible ? 1 : 0;
}
