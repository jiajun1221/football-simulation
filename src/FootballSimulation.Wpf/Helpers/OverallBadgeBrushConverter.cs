using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace FootballSimulation.Wpf.Helpers;

public sealed class OverallBadgeBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var rating = value switch
        {
            int number => number,
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            _ => 0
        };

        var color = string.Equals(parameter?.ToString(), "Foreground", StringComparison.OrdinalIgnoreCase)
            ? OverallBadgeDisplayHelper.GetForeground(rating)
            : OverallBadgeDisplayHelper.GetBackground(rating);
        return (Brush)new BrushConverter().ConvertFromString(color)!;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public static class OverallBadgeDisplayHelper
{
    public static string GetBackground(int rating)
    {
        return rating switch
        {
            >= 90 => "#FF9800",
            >= 80 => "#FFD700",
            >= 70 => "#C0C0C0",
            >= 60 => "#CD7F32",
            _ => "#FFFFFF"
        };
    }

    public static string GetForeground(int rating) => "#172033";
}
