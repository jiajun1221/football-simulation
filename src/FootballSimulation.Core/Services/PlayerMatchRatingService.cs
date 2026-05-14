using FootballSimulation.Models;

namespace FootballSimulation.Services;

public static class PlayerMatchRatingService
{
    public static double CalculateContextualRating(Match match, PlayerMatchPerformance performance, double? baseRating = null)
    {
        var rating = baseRating ?? performance.Rating;
        var goalsConceded = GetGoalsConceded(match, performance.TeamName);
        var player = FindPlayer(match, performance);

        rating += performance.Position switch
        {
            Position.Goalkeeper => GetGoalkeeperAdjustment(match, performance, goalsConceded),
            Position.Defender => GetDefenderAdjustment(match, performance, player, goalsConceded),
            Position.Midfielder when IsDefensiveMidfielder(player) => GetDefenderAdjustment(match, performance, player, goalsConceded) * 0.75,
            Position.Midfielder => -goalsConceded * 0.02,
            _ => 0.0
        };

        rating = ApplyConcessionCaps(performance, goalsConceded, rating);
        return Math.Round(Math.Clamp(rating, 1.0, 10.0), 1);
    }

    public static int GetGoalsConceded(Match match, string teamName)
    {
        if (string.Equals(teamName, match.HomeTeam.Name, StringComparison.OrdinalIgnoreCase))
        {
            return match.AwayScore;
        }

        if (string.Equals(teamName, match.AwayTeam.Name, StringComparison.OrdinalIgnoreCase))
        {
            return match.HomeScore;
        }

        return 0;
    }

    private static double GetDefenderAdjustment(Match match, PlayerMatchPerformance performance, Player? player, int goalsConceded)
    {
        var defensiveMultiplier = GetDefensiveActionMultiplier(performance, player);
        var defensiveBonus = Math.Min(
            1.80,
            (performance.Tackles * 0.08 +
                performance.Interceptions * 0.08 +
                performance.Blocks * 0.10 +
                performance.Clearances * 0.06 +
                performance.AerialDuelsWon * 0.06 +
                performance.Recoveries * 0.07 +
                performance.GoalLineClearances * 0.18) * defensiveMultiplier);
        var cleanSheetBonus = goalsConceded == 0 ? 0.55 : 0.0;
        var teamDefensiveBonus = GetTeamDefensiveBonus(match, performance.TeamName, goalsConceded);
        var directErrorPenalty =
            performance.ErrorsLeadingToShot * 0.15 +
            performance.ErrorsLeadingToGoal * 0.35 +
            performance.PenaltiesConceded * 0.30 +
            Math.Max(0, performance.RedCards) * 0.30;
        var concessionPenalty = goalsConceded * 0.06 + Math.Max(0, goalsConceded - 2) * 0.06;

        return cleanSheetBonus + teamDefensiveBonus + defensiveBonus - concessionPenalty - directErrorPenalty;
    }

    private static double GetGoalkeeperAdjustment(Match match, PlayerMatchPerformance performance, int goalsConceded)
    {
        var cleanSheetBonus = goalsConceded == 0 ? 0.65 : 0.0;
        var saveBonus = Math.Min(performance.Saves * 0.11, 0.90);
        var teamDefensiveBonus = GetTeamDefensiveBonus(match, performance.TeamName, goalsConceded) * 0.75;
        var directErrorPenalty = performance.ErrorsLeadingToGoal * 0.35;
        var concessionPenalty = goalsConceded * 0.34 + Math.Max(0, goalsConceded - 2) * 0.16;

        return cleanSheetBonus + saveBonus + teamDefensiveBonus - concessionPenalty - directErrorPenalty;
    }

    private static double ApplyConcessionCaps(PlayerMatchPerformance performance, int goalsConceded, double rating)
    {
        return performance.Position switch
        {
            Position.Goalkeeper when goalsConceded >= 5 => Math.Min(rating, 5.6),
            Position.Goalkeeper when goalsConceded >= 4 => Math.Min(rating, 6.2),
            Position.Goalkeeper when goalsConceded >= 3 => Math.Min(rating, 7.0),
            Position.Goalkeeper when goalsConceded >= 2 && performance.Saves < 5 => Math.Min(rating, 7.5),
            Position.Defender when goalsConceded >= 5 => Math.Min(rating, 6.2),
            Position.Defender when goalsConceded >= 4 => Math.Min(rating, 6.8),
            Position.Defender when goalsConceded >= 3 => Math.Min(rating, 7.3),
            _ => rating
        };
    }

    private static double GetTeamDefensiveBonus(Match match, string teamName, int goalsConceded)
    {
        var opponentStats = GetOpponentStats(match, teamName);
        if (opponentStats is null)
        {
            return 0.0;
        }

        var lowChanceBonus = opponentStats.TotalShots switch
        {
            <= 5 => 0.40,
            <= 8 => 0.30,
            <= 11 => 0.18,
            _ => 0.0
        };

        if (opponentStats.ShotsOnTarget <= 2)
        {
            lowChanceBonus += 0.08;
        }

        if (opponentStats.ExpectedGoals <= 0.8)
        {
            lowChanceBonus += 0.08;
        }

        if (goalsConceded > 0)
        {
            lowChanceBonus *= 0.65;
        }

        return Math.Min(0.45, lowChanceBonus);
    }

    private static MatchTeamStats? GetOpponentStats(Match match, string teamName)
    {
        if (string.Equals(teamName, match.HomeTeam.Name, StringComparison.OrdinalIgnoreCase))
        {
            return match.AwayStats;
        }

        if (string.Equals(teamName, match.AwayTeam.Name, StringComparison.OrdinalIgnoreCase))
        {
            return match.HomeStats;
        }

        return null;
    }

    private static Player? FindPlayer(Match match, PlayerMatchPerformance performance)
    {
        var team = string.Equals(match.HomeTeam.Name, performance.TeamName, StringComparison.OrdinalIgnoreCase)
            ? match.HomeTeam
            : string.Equals(match.AwayTeam.Name, performance.TeamName, StringComparison.OrdinalIgnoreCase)
                ? match.AwayTeam
                : null;

        return team?.Players.Concat(team.Substitutes)
            .FirstOrDefault(player => string.Equals(player.Name, performance.PlayerName, StringComparison.OrdinalIgnoreCase));
    }

    private static double GetDefensiveActionMultiplier(PlayerMatchPerformance performance, Player? player)
    {
        if (IsDefensiveSpecialist(performance, player))
        {
            return 1.15;
        }

        return performance.Position switch
        {
            Position.Midfielder => 0.90,
            Position.Forward => 0.55,
            _ => 1.0
        };
    }

    private static bool IsDefensiveSpecialist(PlayerMatchPerformance performance, Player? player)
    {
        return performance.Position == Position.Defender || IsDefensiveMidfielder(player);
    }

    private static bool IsDefensiveMidfielder(Player? player)
    {
        var assignedPosition = player?.AssignedPosition ?? string.Empty;
        return assignedPosition.Contains("CDM", StringComparison.OrdinalIgnoreCase) ||
            assignedPosition.Contains("DM", StringComparison.OrdinalIgnoreCase);
    }
}
