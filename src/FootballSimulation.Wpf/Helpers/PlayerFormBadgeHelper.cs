using FootballSimulation.Models;

namespace FootballSimulation.Wpf.Helpers;

internal static class PlayerFormBadgeHelper
{
    public static PlayerFormBadge Create(PlayerFormStatus status)
    {
        return status switch
        {
            PlayerFormStatus.Excellent => new PlayerFormBadge("Excellent", "#10B981", "#FFFFFF"),
            PlayerFormStatus.Good => new PlayerFormBadge("Good", "#4ADE80", "#064E3B"),
            PlayerFormStatus.Poor => new PlayerFormBadge("Poor", "#FB923C", "#FFFFFF"),
            PlayerFormStatus.VeryPoor => new PlayerFormBadge("Very Poor", "#EF4444", "#FFFFFF"),
            _ => new PlayerFormBadge("Average", "#FACC15", "#1F2937")
        };
    }
}

internal sealed record PlayerFormBadge(string Text, string Background, string Foreground);
