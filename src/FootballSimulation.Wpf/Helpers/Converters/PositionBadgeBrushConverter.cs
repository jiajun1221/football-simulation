using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace FootballSimulation.Wpf.Helpers;

public sealed class PositionBadgeBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var position = value?.ToString()?.Trim().ToUpperInvariant() ?? string.Empty;
        var color = position switch
        {
            "ST" or "CF" => "#EF4444",
            "LW" or "RW" or "LM" or "RM" => "#F87171",
            "CAM" => "#F97316",
            "CM" => "#FACC15",
            "CDM" => "#A16207",
            "CB" => "#2563EB",
            "LB" or "RB" => "#60A5FA",
            "GK" => "#7C3AED",
            _ => "#64748B"
        };

        return (Brush)new BrushConverter().ConvertFromString(color)!;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
