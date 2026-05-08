using FootballSimulation.Models;

namespace FootballSimulation.Wpf.Helpers;

public static class PlayerGrowthDisplayHelper
{
    public static string CreateGrowthText(Player player)
    {
        if (player.LastMatchOverallIncrease > 0)
        {
            return $"OVR +{player.LastMatchOverallIncrease}";
        }

        return player.GrowthPoints > 0
            ? $"Growth {Math.Min(player.GrowthPoints, 99)}/100"
            : string.Empty;
    }
}
