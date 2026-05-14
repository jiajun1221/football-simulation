namespace FootballSimulation.Wpf.Helpers;

internal static class RatingDisplayHelper
{
    public static string CreateRatingText(double rating)
    {
        return Math.Round(Math.Clamp(rating, 1.0, 10.0), 1).ToString("0.0");
    }

    public static string GetRatingBrush(double rating)
    {
        return rating switch
        {
            >= 8.5 => "#166534",
            >= 7.0 => "#15803D",
            >= 6.0 => "#1E3A8A",
            >= 5.0 => "#C2410C",
            _ => "#B91C1C"
        };
    }

    public static string GetRatingForeground(double rating)
    {
        return "#FFFFFF";
    }
}
