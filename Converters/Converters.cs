using System.Globalization;
using System.Windows.Data;

namespace LeeyesViewer.Converters;

public class ThumbWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double height)
        {
            return Math.Max(70.0, Math.Round(height * 0.68));
        }
        return 120.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class ThumbContainerHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double height)
        {
            return height + 38.0;
        }
        return 210.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
