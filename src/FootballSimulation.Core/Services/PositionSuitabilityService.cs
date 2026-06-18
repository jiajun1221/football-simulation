using FootballSimulation.Models;

namespace FootballSimulation.Services;

public static class PositionSuitabilityService
{
    private static readonly HashSet<string> ExactPositions =
    [
        "LW", "ST", "RW", "CF",
        "LM", "CM", "CAM", "CDM", "RM",
        "LB", "RB", "CB", "LWB", "RWB",
        "GK"
    ];

    public static void EnsurePositionMetadata(Player player, string? assignedPosition = null)
    {
        var normalizedAssigned = NormalizeExactPosition(assignedPosition);
        var preferredPositions = NormalizeExactPositions(player.PreferredPosition);
        var normalizedPreferred = preferredPositions.FirstOrDefault() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedPreferred))
        {
            normalizedPreferred = normalizedAssigned;
        }

        if (string.IsNullOrWhiteSpace(normalizedPreferred))
        {
            normalizedPreferred = GetDefaultExactPosition(player.Position);
        }

        player.PreferredPosition = normalizedPreferred;
        player.AssignedPosition = normalizedAssigned.Length > 0
            ? normalizedAssigned
            : NormalizeExactPosition(player.AssignedPosition);

        if (string.IsNullOrWhiteSpace(player.AssignedPosition))
        {
            player.AssignedPosition = normalizedPreferred;
        }

        player.SecondaryPositions = preferredPositions
            .Skip(1)
            .Concat(player.SecondaryPositions.SelectMany(NormalizeExactPositions))
            .Where(position => position.Length > 0 && position != player.PreferredPosition)
            .Distinct()
            .ToList();
    }

    public static string NormalizeExactPosition(string? position)
    {
        if (string.IsNullOrWhiteSpace(position))
        {
            return string.Empty;
        }

        var normalized = position.Trim().ToUpperInvariant().Replace("-", string.Empty).Replace(" ", string.Empty);
        return ExactPositions.Contains(normalized) ? normalized : string.Empty;
    }

    public static IReadOnlyList<string> NormalizeExactPositions(string? positions)
    {
        if (string.IsNullOrWhiteSpace(positions))
        {
            return [];
        }

        return positions
            .Split(['/', ',', '|', ';', '\\', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeExactPosition)
            .Where(position => position.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<string> GetNaturalExactPositions(Player player)
    {
        EnsurePositionMetadata(player);
        return new[] { player.PreferredPosition }
            .Concat(player.SecondaryPositions)
            .SelectMany(NormalizeExactPositions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string GetDefaultExactPosition(Position position)
    {
        return position switch
        {
            Position.Goalkeeper => "GK",
            Position.Defender => "CB",
            Position.Midfielder => "CM",
            Position.Forward => "ST",
            _ => "CM"
        };
    }

    public static double GetEffectivenessMultiplier(Player player)
    {
        EnsurePositionMetadata(player);

        var penalty = GetOutOfPositionPenalty(player, player.AssignedPosition);
        if (penalty <= 0)
        {
            return 1.0;
        }

        var baseOverall = Math.Max(1, GetBaseOverall(player));
        return Math.Clamp((baseOverall - penalty) / (double)baseOverall, 0.70, 1.0);
    }

    public static int GetEffectiveOverall(Player player)
    {
        var baseOverall = GetBaseOverall(player);

        return (int)Math.Round(baseOverall * GetEffectivenessMultiplier(player));
    }

    public static bool IsOutOfPosition(Player player)
    {
        EnsurePositionMetadata(player);
        return GetEffectivenessMultiplier(player) < 1.0;
    }

    public static bool IsGoalkeeperCapable(Player player)
    {
        EnsurePositionMetadata(player);
        return player.Position == Position.Goalkeeper ||
            GetNaturalExactPositions(player).Contains("GK", StringComparer.OrdinalIgnoreCase);
    }

    public static int GetOutOfPositionPenalty(Player player, string? assignedPosition = null)
    {
        EnsurePositionMetadata(player);
        var slot = NormalizeExactPosition(assignedPosition) is { Length: > 0 } normalized
            ? normalized
            : player.AssignedPosition;
        if (string.IsNullOrWhiteSpace(slot))
        {
            return 0;
        }

        var naturalPositions = GetNaturalExactPositions(player);
        if (naturalPositions.Count > 0 &&
            naturalPositions[0].Equals(slot, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (IsFullyAdaptedCenterForwardRole(naturalPositions, slot))
        {
            return 0;
        }

        if (naturalPositions.Skip(1).Contains(slot, StringComparer.OrdinalIgnoreCase))
        {
            return 0;
        }

        return naturalPositions
            .Select(position => GetPositionPenalty(position, slot))
            .DefaultIfEmpty(15)
            .Min();
    }

    private static int GetPositionPenalty(string playerPosition, string slot)
    {
        if (playerPosition.Equals(slot, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return slot switch
        {
            "GK" => 99,
            "CB" => playerPosition switch
            {
                "RB" or "LB" => 4,
                "RWB" or "LWB" => 6,
                "CDM" => 8,
                _ => 99
            },
            "RB" or "LB" => playerPosition switch
            {
                "RWB" or "LWB" => 2,
                "CB" => 4,
                "CDM" => 7,
                _ => 99
            },
            "RWB" or "LWB" => playerPosition switch
            {
                "RB" or "LB" => 2,
                "RM" or "LM" => 4,
                "CB" => 6,
                "RW" or "LW" => 7,
                _ => 99
            },
            "CDM" => playerPosition switch
            {
                "CM" => 2,
                "CB" => 5,
                "CAM" => 8,
                _ => 99
            },
            "CM" => playerPosition switch
            {
                "CDM" => 2,
                "CAM" => 3,
                "LM" or "RM" => 4,
                _ => 99
            },
            "CAM" => playerPosition switch
            {
                "CM" => 2,
                "CF" => 0,
                "LW" or "RW" or "LM" or "RM" => 4,
                _ => 99
            },
            "LW" or "RW" => playerPosition switch
            {
                "LM" or "RM" => 2,
                "CAM" or "CF" => 3,
                "ST" => 5,
                "LW" or "RW" => 4,
                _ => 99
            },
            "LM" or "RM" => playerPosition switch
            {
                "LW" or "RW" => 2,
                "LWB" or "RWB" or "LB" or "RB" => 5,
                "CM" or "CAM" => 4,
                _ => 99
            },
            "ST" => playerPosition switch
            {
                "CF" => 0,
                "CAM" => 5,
                "LW" or "RW" => 6,
                _ => 99
            },
            "CF" => playerPosition switch
            {
                "ST" or "CAM" => 3,
                "LW" or "RW" => 6,
                _ => 99
            },
            _ => 15
        };
    }

    private static bool IsFullyAdaptedCenterForwardRole(IEnumerable<string> naturalPositions, string slot)
    {
        return slot is "ST" or "CAM" &&
            naturalPositions.Contains("CF", StringComparer.OrdinalIgnoreCase);
    }

    private static int GetBaseOverall(Player player)
    {
        return player.OverallRating > 0
            ? player.OverallRating
            : PlayerOverallCalculator.CalculateOverall(player);
    }
}
