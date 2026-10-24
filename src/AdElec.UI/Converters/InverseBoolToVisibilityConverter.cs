using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AdElec.UI.Converters;

/// <summary>Convierte bool → Visibility de forma inversa: true=Collapsed, false=Visible.</summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Collapsed;
}
