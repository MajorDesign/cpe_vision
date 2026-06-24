using System.Globalization;
using System.Windows.Data;

namespace VideoWall.Converters
{
    /// <summary>
    /// Soma todos os valores numéricos recebidos. Usado para posicionar os
    /// elementos no Canvas como (coordenada + deslocamento), de modo que cada
    /// janela de saída mostre o recorte correto da parede virtual.
    /// </summary>
    public class SumConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
        {
            double sum = 0;

            foreach (var value in values)
            {
                if (value is double d)
                    sum += d;
                else if (value != null && double.TryParse(value.ToString(), NumberStyles.Any, culture, out var parsed))
                    sum += parsed;
            }

            return sum;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
