using System.Globalization;

namespace SkinnyB.Converters;

public class DivideBy100Converter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double d) return Math.Clamp(d / 100.0, 0, 1);
        return 0.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
