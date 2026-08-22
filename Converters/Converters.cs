using System.Globalization;
using System.Windows.Data;

namespace LeeyesViewer.Converters;

public class ThumbWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double height)
        {
            return Math.Max(50.0, Math.Round(height * 0.72));
        }
        return 114.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
