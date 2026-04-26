using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace SkinnyB.Converters;

public class BoolToOverGoalColorConverter : IValueConverter
{
    private static readonly Color Normal = Color.FromArgb("#52B788");

    private static readonly Color OverGoal = Color.FromArgb("#52F29A");

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool isOverGoal && isOverGoal)
            return OverGoal;

        return Normal;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}