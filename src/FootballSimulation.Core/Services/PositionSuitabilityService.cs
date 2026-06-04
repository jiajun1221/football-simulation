using FootballSimulation.Models;

namespace FootballSimulation.Services;

public static class PositionSuitabilityService
{
    private static readonly HashSet<string> ExactPositions =
    [
        "LW", "ST", "RW", "CF",
        "CM", "CAM", "CDM",
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

        if (player.AssignedPosition == player.PreferredPosition ||
            player.SecondaryPositions.Contains(player.AssignedPosition, StringComparer.OrdinalIgnoreCase))
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

    public static bool IsGoalkeeperCapable(Player player)
    {
        EnsurePositionMetadata(player);
        return player.Position == Position.Goalkeeper ||
            GetNaturalExactPositions(player).Contains("GK", StringComparer.OrdinalIgnoreCase);
    }
}
