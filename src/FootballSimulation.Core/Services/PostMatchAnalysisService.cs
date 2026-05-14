using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class PostMatchAnalysisService
{
    public PostMatchSummary CreateSummary(Match match)
    {
        var adjustedPerformances = match.PlayerPerformances
            .Select(performance => CreateAdjustedPerformance(match, performance))
            .OrderByDescending(performance => performance.Rating)
            .ThenByDescending(performance => performance.Goals + performance.Assists)
            .ThenByDescending(performance => performance.Saves)
            .ThenBy(performance => performance.YellowCards + performance.RedCards + performance.Fouls + performance.Offsides)
            .ToList();

        var manOfTheMatch = adjustedPerformances.FirstOrDefault();

        return new PostMatchSummary
        {
            Match = match,
            PlayerPerformances = adjustedPerformances,
            ManOfTheMatch = manOfTheMatch,
            ManOfTheMatchReason = manOfTheMatch is null
                ? "No player performances were recorded."
                : CreateManOfTheMatchReason(manOfTheMatch, match)
        };
    }

    private static PlayerMatchPerformance CreateAdjustedPerformance(Match match, PlayerMatchPerformance performance)
    {
        var adjusted = CopyPerformance(performance);

        adjusted.Rating = Math.Round(Math.Clamp(adjusted.Rating, 1.0, 10.0), 1);

        return adjusted;
    }

    private static string CreateManOfTheMatchReason(PlayerMatchPerformance performance, Match match)
    {
        var goalsConceded = PlayerMatchRatingService.GetGoalsConceded(match, performance.TeamName);

        if (performance.Goals > 0 && performance.Assists > 0)
        {
            return $"{performance.PlayerName} decided the match with {performance.Goals} goal(s) and {performance.Assists} assist(s).";
        }

        if (performance.Goals > 0)
        {
            return $"{performance.PlayerName} led the scoring with {performance.Goals} goal(s).";
        }

        if (performance.Assists > 0)
        {
            return $"{performance.PlayerName} created the key moments with {performance.Assists} assist(s).";
        }

        if (performance.Position == Position.Goalkeeper && performance.Saves >= 8 && goalsConceded <= 2)
        {
            return $"{performance.PlayerName} earned it with {performance.Saves} saves despite heavy pressure.";
        }

        if (performance.Position == Position.Goalkeeper && goalsConceded == 0)
        {
            return $"{performance.PlayerName} protected the clean sheet with {performance.Saves} save(s).";
        }

        if (performance.Saves > 0)
        {
            return $"{performance.PlayerName} made {performance.Saves} important save(s).";
        }

        return $"{performance.PlayerName} produced the most complete performance with a {performance.Rating:0.0} rating.";
    }

    private static PlayerMatchPerformance CopyPerformance(PlayerMatchPerformance performance)
    {
        return new PlayerMatchPerformance
        {
            PlayerName = performance.PlayerName,
            TeamName = performance.TeamName,
            Position = performance.Position,
            Rating = performance.Rating,
            Goals = performance.Goals,
            Assists = performance.Assists,
            Shots = performance.Shots,
            ShotsOnTarget = performance.ShotsOnTarget,
            Saves = performance.Saves,
            KeyPasses = performance.KeyPasses,
            Tackles = performance.Tackles,
            Interceptions = performance.Interceptions,
            Blocks = performance.Blocks,
            Clearances = performance.Clearances,
            AerialDuelsWon = performance.AerialDuelsWon,
            Recoveries = performance.Recoveries,
            GoalLineClearances = performance.GoalLineClearances,
            ErrorsLeadingToShot = performance.ErrorsLeadingToShot,
            ErrorsLeadingToGoal = performance.ErrorsLeadingToGoal,
            PenaltiesConceded = performance.PenaltiesConceded,
            Fouls = performance.Fouls,
            YellowCards = performance.YellowCards,
            RedCards = performance.RedCards,
            Offsides = performance.Offsides,
            Injuries = performance.Injuries,
            FatigueAtStart = performance.FatigueAtStart,
            FatigueAtEnd = performance.FatigueAtEnd,
            WasSubstitute = performance.WasSubstitute,
            WasSubbedOn = performance.WasSubbedOn,
            WasSubbedOff = performance.WasSubbedOff,
            SubstitutionMinute = performance.SubstitutionMinute
        };
    }
}
