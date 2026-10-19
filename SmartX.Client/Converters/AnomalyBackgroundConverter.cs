using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SmartX.Client.Converters;

public class AnomalyBackgroundConverter : IValueConverter
{
    private static readonly Brush AnomalyBrush = new SolidColorBrush(Color.FromArgb(40, 239, 68, 68));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool isAnomalous && isAnomalous ? AnomalyBrush : Brushes.Transparent;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
