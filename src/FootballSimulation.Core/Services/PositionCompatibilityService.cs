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

        var naturalPositions = PositionSuitabilityService.GetNaturalExactPositions(player);
        if (naturalPositions.Count > 0 &&
            string.Equals(naturalPositions[0], slot, StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (naturalPositions.Skip(1).Contains(slot, StringComparer.OrdinalIgnoreCase))
        {
            return 85;
        }

        return GetAdjacentRoleScore(naturalPositions, slot);
    }

    public static bool IsReasonableFit(Player player, string exactPosition)
    {
        return GetCompatibilityScore(player, exactPosition) > Impossible;
    }

    public static bool CanPlayPosition(Player player, string exactPosition)
    {
        var slot = PositionSuitabilityService.NormalizeExactPosition(exactPosition);
        return !string.IsNullOrWhiteSpace(slot) &&
            PositionSuitabilityService.GetNaturalExactPositions(player).Contains(slot, StringComparer.OrdinalIgnoreCase);
    }

    public static bool CanOccupySlot(Player player, string exactPosition, bool allowOutOfPosition = true)
    {
        var slot = PositionSuitabilityService.NormalizeExactPosition(exactPosition);
        if (string.IsNullOrWhiteSpace(slot))
        {
            return allowOutOfPosition;
        }

        if (slot == "GK")
        {
            return PositionSuitabilityService.IsGoalkeeperCapable(player);
        }

        if (PositionSuitabilityService.IsGoalkeeperCapable(player))
        {
            return false;
        }

        return allowOutOfPosition || GetCompatibilityScore(player, slot) > Impossible;
    }

    private static int GetAdjacentRoleScore(IEnumerable<string> naturalPositions, string slot)
    {
        var positions = naturalPositions
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
                "RM" => 60,
                "RW" => 40,
                "CB" => 45,
                _ => Impossible
            },
            "LWB" => playerPosition switch
            {
                "LWB" => 100,
                "LB" => 90,
                "LM" => 60,
                "LW" => 40,
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
            "LM" => playerPosition switch
            {
                "LM" => 100,
                "LW" => 90,
                "LWB" or "LB" => 65,
                "CM" or "CAM" => 55,
                _ => Impossible
            },
            "RM" => playerPosition switch
            {
                "RM" => 100,
                "RW" => 90,
                "RWB" or "RB" => 65,
                "CM" or "CAM" => 55,
                _ => Impossible
            },
            "CAM" => playerPosition switch
            {
                "CAM" => 100,
                "CM" => 60,
                "LM" or "RM" or "LW" or "RW" or "CF" => 60,
                _ => Impossible
            },
            "LW" => playerPosition switch
            {
                "LW" => 100,
                "LM" => 90,
                "RW" => 60,
                "ST" => 45,
                "LWB" => 60,
                "CAM" or "CF" => 60,
                _ => Impossible
            },
            "RW" => playerPosition switch
            {
                "RW" => 100,
                "RM" => 90,
                "LW" => 60,
                "ST" => 45,
                "RWB" => 60,
                "CAM" or "CF" => 60,
                _ => Impossible
            },
            "ST" => playerPosition switch
            {
                "ST" => 100,
                "CF" => 90,
                "CAM" => 55,
                "LW" or "RW" => 45,
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
