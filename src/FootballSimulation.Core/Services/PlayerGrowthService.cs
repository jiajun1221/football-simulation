using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class PlayerGrowthService
{
    private const int GrowthThreshold = 100;

    public void ApplyMatchGrowth(Match match)
    {
        ResetLastMatchGrowth(match.HomeTeam);
        ResetLastMatchGrowth(match.AwayTeam);

        foreach (var performance in match.PlayerPerformances)
        {
            var team = string.Equals(performance.TeamName, match.HomeTeam.Name, StringComparison.OrdinalIgnoreCase)
                ? match.HomeTeam
                : string.Equals(performance.TeamName, match.AwayTeam.Name, StringComparison.OrdinalIgnoreCase)
                    ? match.AwayTeam
                    : null;
            var player = team?.Players.Concat(team.Substitutes)
                .FirstOrDefault(candidate => string.Equals(candidate.Name, performance.PlayerName, StringComparison.OrdinalIgnoreCase));

            if (player is null || !PlayerPlayed(match, performance))
            {
                continue;
            }

            ApplyPlayerGrowth(match, team!, player, performance);
        }
    }

    private static void ResetLastMatchGrowth(Team team)
    {
        foreach (var player in team.Players.Concat(team.Substitutes))
        {
            player.LastMatchGrowthPoints = 0;
            player.LastMatchOverallIncrease = 0;
        }
    }

    private static void ApplyPlayerGrowth(Match match, Team team, Player player, PlayerMatchPerformance performance)
    {
        var growthPoints = CalculateGrowthPoints(match, team, player, performance);
        player.LastMatchGrowthPoints = growthPoints;
        if (growthPoints <= 0)
        {
            return;
        }

        player.GrowthPoints += growthPoints;
        if (player.GrowthPoints < GrowthThreshold || !CanGrow(player))
        {
            return;
        }

        IncreaseOverall(player);
        player.GrowthPoints -= GrowthThreshold;
        player.LastMatchOverallIncrease = 1;
    }

    private static int CalculateGrowthPoints(Match match, Team team, Player player, PlayerMatchPerformance performance)
    {
        var basePoints = performance.Rating switch
        {
            >= 8.5 => 40,
            >= 7.3 => 20,
            >= 6.2 => 5,
            _ => 0
        };

        if (basePoints == 0)
        {
            return 0;
        }

        var contributionBonus = player.Position == Position.Goalkeeper
            ? CalculateGoalkeeperBonus(match, team, performance)
            : CalculateOutfieldBonus(performance);
        var formBonus = player.FormStatus switch
        {
            PlayerFormStatus.Excellent => 10,
            PlayerFormStatus.Good => 5,
            PlayerFormStatus.Poor or PlayerFormStatus.VeryPoor => -5,
            _ => 0
        };
        var minutesMultiplier = GetMinutesPlayed(match, performance) switch
        {
            >= 75 => 1.0,
            >= 45 => 0.75,
            >= 20 => 0.50,
            _ => 0.25
        };
        var ageMultiplier = GetAgeGrowthMultiplier(player);
        var total = (basePoints + contributionBonus + formBonus) * minutesMultiplier * ageMultiplier;

        return Math.Clamp((int)Math.Round(total), 0, 60);
    }

    private static int CalculateOutfieldBonus(PlayerMatchPerformance performance)
    {
        var defensiveContributions = performance.Tackles +
            performance.Interceptions +
            performance.Blocks +
            performance.Clearances;

        return Math.Min(20, performance.Goals * 10 + performance.Assists * 8 + Math.Min(10, defensiveContributions * 2));
    }

    private static int CalculateGoalkeeperBonus(Match match, Team team, PlayerMatchPerformance performance)
    {
        var goalsConceded = team == match.HomeTeam ? match.AwayScore : match.HomeScore;
        var cleanSheetBonus = goalsConceded == 0 ? 10 : 0;

        return Math.Min(20, cleanSheetBonus + performance.Saves * 3);
    }

    private static int GetMinutesPlayed(Match match, PlayerMatchPerformance performance)
    {
        var matchMinutes = Math.Clamp(match.CurrentMinute, 1, 90);
        if (performance.WasSubstitute && performance.WasSubbedOn)
        {
            return Math.Max(1, matchMinutes - (performance.SubstitutionMinute ?? matchMinutes) + 1);
        }

        if (performance.WasSubbedOff)
        {
            return Math.Clamp(performance.SubstitutionMinute ?? matchMinutes, 1, matchMinutes);
        }

        return matchMinutes;
    }

    private static bool PlayerPlayed(Match match, PlayerMatchPerformance performance)
    {
        return !performance.WasSubstitute ||
            performance.WasSubbedOn ||
            performance.SubstitutionMinute.HasValue ||
            GetMinutesPlayed(match, performance) > 0;
    }

    private static double GetAgeGrowthMultiplier(Player player)
    {
        return player.Age switch
        {
            <= 21 => 1.35,
            <= 24 => 1.15,
            >= 32 => 0.55,
            >= 29 => 0.75,
            >= 27 => 0.90,
            _ => 1.0
        };
    }

    private static bool CanGrow(Player player)
    {
        return player.OverallRating < Math.Min(99, GetPotentialCap(player));
    }

    private static int GetPotentialCap(Player player)
    {
        if (player.PotentialOverall.HasValue)
        {
            return Math.Max(player.OverallRating, player.PotentialOverall.Value);
        }

        if (player.Age is null)
        {
            return 99;
        }

        var baseOverall = player.BaseOverallRating > 0 ? player.BaseOverallRating : player.OverallRating;
        return player.Age switch
        {
            <= 21 => Math.Min(99, baseOverall + 10),
            <= 24 => Math.Min(99, baseOverall + 7),
            <= 27 => Math.Min(99, baseOverall + 5),
            <= 30 => Math.Min(99, baseOverall + 3),
            _ => Math.Min(99, baseOverall + 1)
        };
    }

    private static void IncreaseOverall(Player player)
    {
        player.OverallRating = Math.Min(99, player.OverallRating + 1);
        player.Attack = Math.Min(99, player.Attack + 1);
        player.Defense = Math.Min(99, player.Defense + 1);
        player.Passing = Math.Min(99, player.Passing + 1);
        player.Finishing = Math.Min(99, player.Finishing + 1);
    }
}
