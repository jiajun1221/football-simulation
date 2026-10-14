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
            >= 9.0 => "#10B981",
            >= 7.5 => "#4ADE80",
            >= 6.0 => "#FACC15",
            >= 5.0 => "#FB923C",
            _ => "#EF4444"
        };
    }

    public static string GetRatingForeground(double rating)
    {
        return rating is >= 6.0 and < 9.0
            ? "#1F2937"
            : "#FFFFFF";
    }
}
