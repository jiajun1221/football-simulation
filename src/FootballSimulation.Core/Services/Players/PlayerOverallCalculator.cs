using FootballSimulation.Models;

namespace FootballSimulation.Services;

public static class PlayerOverallCalculator
{
    public static int CalculateOverall(Player player)
    {
        PositionSuitabilityService.EnsurePositionMetadata(player);
        var weights = GetWeights(player);
        var weightedOverall =
            player.Attack * weights.Attack +
            player.Defense * weights.Defense +
            player.Passing * weights.Passing +
            player.Finishing * weights.Finishing;

        return Math.Clamp((int)Math.Round(weightedOverall), 1, 99);
    }

    public static int GrowAttributesTowardNextOverall(Player player, int targetOverall)
    {
        var startingOverall = CalculateOverall(player);
        var cappedTarget = Math.Clamp(targetOverall, startingOverall, 99);
        var priorities = GetGrowthPriorities(player);
        var attempts = 0;

        while (CalculateOverall(player) < cappedTarget && attempts < 16)
        {
            var attribute = priorities[attempts % priorities.Count];
            IncreaseAttribute(player, attribute);
            attempts++;
        }

        player.OverallRating = Math.Max(player.OverallRating, CalculateOverall(player));
        return Math.Max(0, player.OverallRating - startingOverall);
    }

    private static AttributeWeights GetWeights(Player player)
    {
        return GetPrimaryExactPosition(player) switch
        {
            "ST" => new(0.35, 0.10, 0.15, 0.40),
            "RW" or "LW" => new(0.38, 0.08, 0.25, 0.29),
            "CAM" => new(0.30, 0.10, 0.40, 0.20),
            "CM" => new(0.25, 0.25, 0.40, 0.10),
            "CDM" => new(0.15, 0.45, 0.35, 0.05),
            "CB" => new(0.12, 0.55, 0.25, 0.08),
            "LB" or "RB" => new(0.18, 0.42, 0.30, 0.10),
            "GK" => new(0.00, 1.00, 0.00, 0.00),
            _ => player.Position switch
            {
                Position.Forward => new(0.35, 0.10, 0.15, 0.40),
                Position.Midfielder => new(0.24, 0.28, 0.38, 0.10),
                Position.Defender => new(0.12, 0.52, 0.28, 0.08),
                Position.Goalkeeper => new(0.00, 1.00, 0.00, 0.00),
                _ => new(0.25, 0.25, 0.25, 0.25)
            }
        };
    }

    private static IReadOnlyList<PlayerAttribute> GetGrowthPriorities(Player player)
    {
        return GetPrimaryExactPosition(player) switch
        {
            "ST" => [PlayerAttribute.Finishing, PlayerAttribute.Attack, PlayerAttribute.Finishing, PlayerAttribute.Attack, PlayerAttribute.Passing],
            "RW" or "LW" => [PlayerAttribute.Attack, PlayerAttribute.Finishing, PlayerAttribute.Passing, PlayerAttribute.Attack],
            "CAM" => [PlayerAttribute.Passing, PlayerAttribute.Attack, PlayerAttribute.Passing, PlayerAttribute.Finishing],
            "CM" => [PlayerAttribute.Passing, PlayerAttribute.Defense, PlayerAttribute.Attack, PlayerAttribute.Passing],
            "CDM" => [PlayerAttribute.Defense, PlayerAttribute.Passing, PlayerAttribute.Defense, PlayerAttribute.Passing],
            "CB" => [PlayerAttribute.Defense, PlayerAttribute.Defense, PlayerAttribute.Passing],
            "LB" or "RB" => [PlayerAttribute.Defense, PlayerAttribute.Passing, PlayerAttribute.Attack],
            "GK" => [PlayerAttribute.Defense, PlayerAttribute.Defense, PlayerAttribute.Passing],
            _ => player.Position switch
            {
                Position.Forward => [PlayerAttribute.Finishing, PlayerAttribute.Attack, PlayerAttribute.Finishing, PlayerAttribute.Passing],
                Position.Midfielder => [PlayerAttribute.Passing, PlayerAttribute.Attack, PlayerAttribute.Defense],
                Position.Defender => [PlayerAttribute.Defense, PlayerAttribute.Passing, PlayerAttribute.Defense],
                Position.Goalkeeper => [PlayerAttribute.Defense, PlayerAttribute.Defense, PlayerAttribute.Passing],
                _ => [PlayerAttribute.Attack, PlayerAttribute.Defense, PlayerAttribute.Passing, PlayerAttribute.Finishing]
            }
        };
    }

    private static string GetPrimaryExactPosition(Player player)
    {
        PositionSuitabilityService.EnsurePositionMetadata(player);
        return !string.IsNullOrWhiteSpace(player.PreferredPosition)
            ? player.PreferredPosition
            : player.AssignedPosition;
    }

    private static void IncreaseAttribute(Player player, PlayerAttribute attribute)
    {
        switch (attribute)
        {
            case PlayerAttribute.Attack:
                player.Attack = Math.Min(99, player.Attack + 1);
                break;
            case PlayerAttribute.Defense:
                player.Defense = Math.Min(99, player.Defense + 1);
                break;
            case PlayerAttribute.Passing:
                player.Passing = Math.Min(99, player.Passing + 1);
                break;
            case PlayerAttribute.Finishing:
                player.Finishing = Math.Min(99, player.Finishing + 1);
                break;
        }
    }

    private sealed record AttributeWeights(double Attack, double Defense, double Passing, double Finishing);

    private enum PlayerAttribute
    {
        Attack,
        Defense,
        Passing,
        Finishing
    }
}
