using System.Globalization;
using System.Windows.Data;

namespace VideoWall.Converters
{
    /// <summary>
    /// Converte a URL (texto) em <see cref="Uri"/> para a propriedade Source do
    /// WebView2. Retorna "about:blank" quando o texto ainda não é uma URL válida,
    /// evitando exceções durante a digitação.
    /// </summary>
    public class StringToUriConverter : IValueConverter
    {
        private static readonly Uri Blank = new("about:blank");

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string text && Uri.TryCreate(text, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == "file"))
            {
                return uri;
            }

            return Blank;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value?.ToString() ?? string.Empty;
        }
    }
}
