using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class LeagueScheduleServiceTests
{
    [Theory]
    [InlineData("premier-league", 38)]
    [InlineData("la-liga", 38)]
    [InlineData("serie-a", 38)]
    [InlineData("bundesliga", 34)]
    [InlineData("ligue-1", 34)]
    public void CreateLeague_GeneratesDoubleRoundRobinSchedule(string leagueId, int expectedRounds)
    {
        var dataService = new LeagueDataService();
        var definition = dataService.GetLeagueDefinition(leagueId);
        var league = new GameSessionService().CreateLeague(definition, dataService.LoadTeams(definition));

        Assert.Equal(expectedRounds, league.Fixtures.Max(fixture => fixture.RoundNumber));
        Assert.Equal(league.Teams.Count * (league.Teams.Count - 1), league.Fixtures.Count);

        foreach (var firstLegFixture in league.Fixtures.Where(fixture => fixture.RoundNumber <= league.Teams.Count - 1))
        {
            Assert.Contains(league.Fixtures, reverseFixture =>
                reverseFixture.RoundNumber == firstLegFixture.RoundNumber + league.Teams.Count - 1 &&
                reverseFixture.HomeTeam == firstLegFixture.AwayTeam &&
                reverseFixture.AwayTeam == firstLegFixture.HomeTeam);
        }
    }
}
