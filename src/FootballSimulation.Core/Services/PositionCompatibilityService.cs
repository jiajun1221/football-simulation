using FootballSimulation.Models;

namespace FootballSimulation.Services;

public static class PositionCompatibilityService
{
    public const int Impossible = -999;
    public const int Emergency = 35;

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

        if (string.Equals(player.PreferredPosition, slot, StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (player.SecondaryPositions.Contains(slot, StringComparer.OrdinalIgnoreCase))
        {
            return 85;
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
            "GK" => playerPosition switch
            {
                "GK" => 100,
                _ => Impossible
            },
            "CB" => playerPosition switch
            {
                "CB" => 100,
                "RB" or "LB" => 55,
                "RWB" or "LWB" => 45,
                "CDM" => Emergency,
                _ => Impossible
            },
            "RB" => playerPosition switch
            {
                "RB" => 100,
                "RWB" => 90,
                "CB" => 55,
                "LB" => 45,
                "CDM" => Emergency,
                _ => Impossible
            },
            "LB" => playerPosition switch
            {
                "LB" => 100,
                "LWB" => 90,
                "CB" => 55,
                "RB" => 45,
                "CDM" => Emergency,
                _ => Impossible
            },
            "RWB" => playerPosition switch
            {
                "RWB" => 100,
                "RB" => 90,
                "RW" => 60,
                "CB" => 45,
                _ => Impossible
            },
            "LWB" => playerPosition switch
            {
                "LWB" => 100,
                "LB" => 90,
                "LW" => 60,
                "CB" => 45,
                _ => Impossible
            },
            "CDM" => playerPosition switch
            {
                "CDM" => 100,
                "CM" => 60,
                "CB" => 55,
                _ => Impossible
            },
            "CM" => playerPosition switch
            {
                "CM" => 100,
                "CAM" or "CDM" => 60,
                _ => Impossible
            },
            "CAM" => playerPosition switch
            {
                "CAM" => 100,
                "CM" => 60,
                "LW" or "RW" or "CF" => 60,
                _ => Impossible
            },
            "LW" => playerPosition switch
            {
                "LW" => 100,
                "RW" => 60,
                "LWB" => 60,
                "CAM" or "CF" => 60,
                _ => Impossible
            },
            "RW" => playerPosition switch
            {
                "RW" => 100,
                "LW" => 60,
                "RWB" => 60,
                "CAM" or "CF" => 60,
                _ => Impossible
            },
            "ST" => playerPosition switch
            {
                "ST" => 100,
                "CF" => 60,
                "LW" or "RW" => Emergency,
                _ => Impossible
            },
            "CF" => playerPosition switch
            {
                "CF" => 100,
                "ST" or "CAM" => 60,
                "LW" or "RW" => Emergency,
                _ => Impossible
            },
            _ => Emergency
        };
    }
}
