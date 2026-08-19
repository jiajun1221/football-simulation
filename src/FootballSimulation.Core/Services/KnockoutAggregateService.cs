using FootballSimulation.Models;

namespace FootballSimulation.Services;

public static class KnockoutAggregateService
{
    public static bool IsTieLevelAfterMatch(League? league, Fixture fixture, Match currentMatch)
    {
        if (!fixture.IsTwoLeggedTie)
        {
            return currentMatch.HomeScore == currentMatch.AwayScore;
        }

        var aggregateScore = GetLiveAggregateScore(
            league,
            fixture,
            currentMatch.HomeScore,
            currentMatch.AwayScore);
        return aggregateScore.HasValue &&
            aggregateScore.Value.HomeScore == aggregateScore.Value.AwayScore;
    }

    public static (int HomeScore, int AwayScore)? GetLiveAggregateScore(
        League? league,
        Fixture fixture,
        int currentHomeScore,
        int currentAwayScore)
    {
        if (!fixture.IsTwoLeggedTie || fixture.LegNumber < 2 || league is null)
        {
            return null;
        }

        var homeAggregate = currentHomeScore;
        var awayAggregate = currentAwayScore;
        foreach (var previousLeg in league.Fixtures.Where(candidate =>
            candidate.IsTwoLeggedTie &&
            candidate.LegNumber < fixture.LegNumber &&
            candidate.IsPlayed &&
            candidate.Result is not null &&
            candidate.KnockoutTieId.Equals(fixture.KnockoutTieId, StringComparison.OrdinalIgnoreCase)))
        {
            homeAggregate += GetTeamScore(previousLeg, fixture.HomeTeam.Name);
            awayAggregate += GetTeamScore(previousLeg, fixture.AwayTeam.Name);
        }

        return (homeAggregate, awayAggregate);
    }

    private static int GetTeamScore(Fixture fixture, string teamName)
    {
        return fixture.HomeTeam.Name.Equals(teamName, StringComparison.OrdinalIgnoreCase)
            ? fixture.Result!.HomeScore
            : fixture.Result!.AwayScore;
    }
}
