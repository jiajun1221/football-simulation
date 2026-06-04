using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class SeasonCompletionService
{
    public bool IsLeagueComplete(League? league)
    {
        return league is not null &&
            league.Fixtures.Count > 0 &&
            league.Fixtures.All(fixture => fixture.IsPlayed);
    }

    public Fixture? GetNextFixtureForTeamOrNull(League? league, Team? selectedTeam)
    {
        if (league is null || selectedTeam is null)
        {
            return null;
        }

        return league.Fixtures
            .Where(fixture => !fixture.IsPlayed && IsTeamInFixture(fixture, selectedTeam))
            .OrderBy(fixture => fixture.RoundNumber)
            .FirstOrDefault();
    }

    private static bool IsTeamInFixture(Fixture fixture, Team team)
    {
        return fixture.HomeTeam == team ||
            fixture.AwayTeam == team ||
            fixture.HomeTeam.Name.Equals(team.Name, StringComparison.OrdinalIgnoreCase) ||
            fixture.AwayTeam.Name.Equals(team.Name, StringComparison.OrdinalIgnoreCase);
    }
}
