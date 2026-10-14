using FootballSimulation.Models;

namespace FootballSimulation.Services;

public static class VeteranDeclineService
{
    public static int ApplySeasonDecline(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (player.Age is not int age)
        {
            return 0;
        }

        var effectiveAge = PositionSuitabilityService.IsGoalkeeperCapable(player)
            ? age - 2
            : age;
        var baseDecline = effectiveAge switch
        {
            < 31 => 0,
            <= 32 => 1,
            <= 34 => 2,
            <= 36 => 3,
            <= 38 => 4,
            _ => 5
        };
        if (baseDecline == 0)
        {
            return 0;
        }

        var formAdjustment = player.FormStatus switch
        {
            PlayerFormStatus.Excellent => -2,
            PlayerFormStatus.Good => -1,
            PlayerFormStatus.Poor => 1,
            PlayerFormStatus.VeryPoor => 2,
            _ => 0
        };
        if (player.CurrentForm >= 75)
        {
            formAdjustment--;
        }
        else if (player.CurrentForm <= 30)
        {
            formAdjustment++;
        }

        var decline = Math.Clamp(baseDecline + formAdjustment, 0, 6);
        if (decline == 0)
        {
            return 0;
        }

        player.OverallRating = Math.Max(40, player.OverallRating - decline);
        player.BaseOverallRating = player.BaseOverallRating > 0
            ? Math.Max(40, player.BaseOverallRating - decline)
            : player.OverallRating;
        player.PotentialOverall = player.PotentialOverall.HasValue
            ? Math.Max(player.OverallRating, player.PotentialOverall.Value - decline)
            : null;

        ReduceCoreAttributes(player, decline);
        player.GrowthPoints = Math.Max(0, player.GrowthPoints - decline * 15);
        player.LastMatchOverallIncrease = 0;
        return decline;
    }

    private static void ReduceCoreAttributes(Player player, int decline)
    {
        player.Attack = Reduce(player.Attack, decline);
        player.Defense = Reduce(player.Defense, decline);
        player.Passing = Reduce(player.Passing, decline);
        player.Finishing = Reduce(player.Finishing, decline);
        player.Shooting = Reduce(player.Shooting, decline);
        player.Dribbling = Reduce(player.Dribbling, decline);
        player.Defending = Reduce(player.Defending, decline);
        player.Physical = Reduce(player.Physical, decline);

        var paceDecline = PositionSuitabilityService.IsGoalkeeperCapable(player)
            ? Math.Max(1, decline - 1)
            : decline + (player.Age >= 35 ? 1 : 0);
        player.Pace = Reduce(player.Pace, paceDecline);
    }

    private static int Reduce(int attribute, int amount)
    {
        return attribute <= 0 ? attribute : Math.Max(1, attribute - amount);
    }
}
