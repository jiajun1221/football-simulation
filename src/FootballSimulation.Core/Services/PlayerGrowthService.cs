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

        var previousOverall = player.OverallRating;
        IncreaseOverall(player);
        PlayerTraitAssignmentService.UnlockOverallMilestoneTraits(player, previousOverall);
        player.GrowthPoints -= GrowthThreshold;
        player.LastMatchOverallIncrease = Math.Max(0, player.OverallRating - previousOverall);
    }

    private static int CalculateGrowthPoints(Match match, Team team, Player player, PlayerMatchPerformance performance)
    {
        var isAtOrAbovePotentialCap = IsAtOrAbovePotentialCap(player);
        if (isAtOrAbovePotentialCap && !CanEarnOverCapGrowth(player, performance))
        {
            return 0;
        }

        var basePoints = performance.Rating switch
        {
            >= 8.5 => 40,
            >= 7.3 => 20,
            >= 6.2 => 5,
            _ => 0
        };

        var minutesPlayed = GetMinutesPlayed(match, performance);
        var youngBaselineGrowthPoints = isAtOrAbovePotentialCap
            ? 0
            : CalculateYoungBaselineGrowthPoints(player, minutesPlayed);
        if (basePoints == 0)
        {
            return youngBaselineGrowthPoints;
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
        var minutesMultiplier = minutesPlayed switch
        {
            >= 75 => 1.0,
            >= 45 => 0.75,
            >= 20 => 0.50,
            _ => 0.25
        };
        var ageMultiplier = GetAgeGrowthMultiplier(player);
        var total = (basePoints + contributionBonus + formBonus) * minutesMultiplier * ageMultiplier;
        if (isAtOrAbovePotentialCap)
        {
            total *= GetOverCapGrowthMultiplier(player);
        }
        else
        {
            total *= GetEmergingProspectPerformanceMultiplier(player, performance);
            total = Math.Max(total, youngBaselineGrowthPoints);
        }

        return Math.Clamp((int)Math.Round(total), 0, GetMatchGrowthPointCap(player));
    }

    private static int CalculateYoungBaselineGrowthPoints(Player player, int minutesPlayed)
    {
        if (player.Age is not < 30)
        {
            return 0;
        }

        var ageBase = player.Age switch
        {
            <= 21 => 4,
            <= 24 => 3,
            <= 27 => 2,
            _ => 1
        };
        var minutesMultiplier = minutesPlayed switch
        {
            >= 75 => 1.0,
            >= 45 => 0.75,
            >= 20 => 0.50,
            _ => 0.25
        };

        var accelerationMultiplier = IsEmergingProspectGrowthWindow(player) ? 2.0 : 1.0;
        return Math.Max(1, (int)Math.Round(ageBase * minutesMultiplier * accelerationMultiplier));
    }

    private static int CalculateOutfieldBonus(PlayerMatchPerformance performance)
    {
        var defensiveContributions = performance.Tackles +
            performance.Interceptions +
            performance.Blocks +
            performance.Clearances +
            performance.AerialDuelsWon +
            performance.Recoveries +
            performance.GoalLineClearances * 2;

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

    private static bool IsEmergingProspectGrowthWindow(Player player)
    {
        return player.Age is < 20 &&
            player.OverallRating is >= 60 and <= 78;
    }

    private static double GetEmergingProspectPerformanceMultiplier(Player player, PlayerMatchPerformance performance)
    {
        if (!IsEmergingProspectGrowthWindow(player))
        {
            return 1.0;
        }

        return performance.Rating switch
        {
            >= 8.5 => 2.45,
            >= 7.3 => 2.05,
            >= 6.2 => 1.55,
            _ => 1.0
        };
    }

    private static int GetMatchGrowthPointCap(Player player)
    {
        return IsEmergingProspectGrowthWindow(player) ? 150 : 60;
    }

    private static bool CanGrow(Player player)
    {
        return player.OverallRating < 99;
    }

    private static int GetPotentialCap(Player player)
    {
        if (player.PotentialOverall.HasValue)
        {
            return Math.Min(99, player.PotentialOverall.Value);
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

    private static bool IsAtOrAbovePotentialCap(Player player)
    {
        return player.OverallRating >= GetPotentialCap(player);
    }

    private static bool CanEarnOverCapGrowth(Player player, PlayerMatchPerformance performance)
    {
        return player.Age is < 30 && performance.Rating >= 8.5;
    }

    private static double GetOverCapGrowthMultiplier(Player player)
    {
        return player.Age switch
        {
            <= 21 => 0.18,
            <= 24 => 0.10,
            <= 27 => 0.06,
            < 30 => 0.03,
            _ => 0.0
        };
    }

    private static void IncreaseOverall(Player player)
    {
        var targetOverall = Math.Min(99, player.OverallRating + 1);
        PlayerOverallCalculator.GrowAttributesTowardNextOverall(player, targetOverall);
    }
}
