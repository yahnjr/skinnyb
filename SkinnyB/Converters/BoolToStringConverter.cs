namespace SkinnyB.Converters;

public class BoolToStringConverter : IValueConverter
{
    public string TrueValue { get; set; } = "";
    public string FalseValue { get; set; } = "";

    public object Convert(object? value, Type targetType, object? parameter,
        System.Globalization.CultureInfo culture)
        => value is true ? TrueValue : FalseValue;

    public object ConvertBack(object? value, Type targetType, object? parameter,
        System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}