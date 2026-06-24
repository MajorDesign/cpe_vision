using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VideoWall.Converters
{
    /// <summary>
    /// Retorna <see cref="Visibility.Visible"/> quando o valor é nulo e
    /// <see cref="Visibility.Collapsed"/> caso contrário. Usado para exibir um
    /// texto temporário enquanto o primeiro frame da captura não chegou.
    /// </summary>
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value == null ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
