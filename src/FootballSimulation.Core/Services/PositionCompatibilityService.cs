using FootballSimulation.Models;

namespace FootballSimulation.Services;

public static class PositionCompatibilityService
{
    public const int Impossible = 0;
    public const int Emergency = 20;

    public static int GetCompatibilityScore(Player player, string exactPosition)
    {
        PositionSuitabilityService.EnsurePositionMetadata(player);
        var slot = PositionSuitabilityService.NormalizeExactPosition(exactPosition);
        if (string.IsNullOrWhiteSpace(slot))
        {
            return Emergency;
        }

        if (slot == "GK")
        {
            return PositionSuitabilityService.IsGoalkeeperCapable(player) ? 100 : Impossible;
        }

        if (PositionSuitabilityService.IsGoalkeeperCapable(player))
        {
            return Impossible;
        }

        if (player.PreferredPosition == slot)
        {
            return 100;
        }

        if (player.SecondaryPositions.Contains(slot))
        {
            return 80;
        }

        return GetAdjacentRoleScore(player, slot);
    }

    public static bool IsReasonableFit(Player player, string exactPosition)
    {
        return GetCompatibilityScore(player, exactPosition) > Impossible;
    }

    private static int GetAdjacentRoleScore(Player player, string slot)
    {
        var positions = new[] { player.PreferredPosition }
            .Concat(player.SecondaryPositions)
            .Where(position => !string.IsNullOrWhiteSpace(position))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return positions
            .Select(position => GetAdjacentRoleScore(position, slot))
            .DefaultIfEmpty(Impossible)
            .Max();
    }

    private static int GetAdjacentRoleScore(string playerPosition, string slot)
    {
        return slot switch
        {
            "CB" => playerPosition switch
            {
                "CB" => 100,
                "RB" or "LB" => 55,
                "CDM" => 45,
                _ => Impossible
            },
            "RB" => playerPosition switch
            {
                "RB" => 100,
                "CB" => 60,
                "LB" => 35,
                "CDM" => 35,
                _ => Impossible
            },
            "LB" => playerPosition switch
            {
                "LB" => 100,
                "CB" => 60,
                "RB" => 35,
                "CDM" => 35,
                _ => Impossible
            },
            "CDM" => playerPosition switch
            {
                "CDM" => 100,
                "CM" => 85,
                "CB" => 50,
                "CAM" => 35,
                _ => Impossible
            },
            "CM" => playerPosition switch
            {
                "CM" => 100,
                "CAM" or "CDM" => 75,
                "LW" or "RW" => 35,
                _ => Impossible
            },
            "CAM" => playerPosition switch
            {
                "CAM" => 100,
                "CM" => 75,
                "LW" or "RW" => 55,
                "ST" => 45,
                _ => Impossible
            },
            "LW" => playerPosition switch
            {
                "LW" => 100,
                "RW" => 85,
                "CAM" => 65,
                "ST" => 45,
                _ => Impossible
            },
            "RW" => playerPosition switch
            {
                "RW" => 100,
                "LW" => 85,
                "CAM" => 65,
                "ST" => 45,
                _ => Impossible
            },
            "ST" => playerPosition switch
            {
                "ST" => 100,
                "LW" or "RW" => 40,
                "CAM" => 35,
                _ => Impossible
            },
            _ => Emergency
        };
    }
}
