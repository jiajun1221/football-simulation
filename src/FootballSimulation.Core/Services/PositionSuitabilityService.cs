using FootballSimulation.Models;

namespace FootballSimulation.Services;

public static class PositionSuitabilityService
{
    private static readonly HashSet<string> ExactPositions =
    [
        "LW", "ST", "RW",
        "CM", "CAM", "CDM",
        "LB", "RB", "CB",
        "GK"
    ];

    public static void EnsurePositionMetadata(Player player, string? assignedPosition = null)
    {
        var normalizedAssigned = NormalizeExactPosition(assignedPosition);
        var normalizedPreferred = NormalizeExactPosition(player.PreferredPosition);

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

        player.SecondaryPositions = player.SecondaryPositions
            .Select(NormalizeExactPosition)
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

        if (player.AssignedPosition == player.PreferredPosition ||
            player.SecondaryPositions.Contains(player.AssignedPosition))
        {
            return 1.0;
        }

        return 0.70;
    }

    public static int GetEffectiveOverall(Player player)
    {
        var baseOverall = PlayerOverallCalculator.CalculateOverall(player);

        return (int)Math.Round(baseOverall * GetEffectivenessMultiplier(player));
    }

    public static bool IsOutOfPosition(Player player)
    {
        EnsurePositionMetadata(player);
        return GetEffectivenessMultiplier(player) < 1.0;
    }
}
