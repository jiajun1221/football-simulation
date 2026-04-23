using FootballSimulation.Data;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class GameSessionServiceTests
{
    [Fact]
    public void CreatePremierLeague_UsesPremierLeagueName()
    {
        var seedDataService = new LeagueSeedDataService();
        var gameSessionService = new GameSessionService();
        var teams = seedDataService.CreateLeagueTeams();

        var league = gameSessionService.CreatePremierLeague(teams);

        Assert.Equal(GameSessionService.PremierLeagueName, league.Name);
        Assert.Equal(teams.Count, league.Teams.Count);
    }

    [Fact]
    public void FindNextFixtureForTeam_ReturnsFixtureContainingSelectedTeam()
    {
        var seedDataService = new LeagueSeedDataService();
        var gameSessionService = new GameSessionService();
        var teams = seedDataService.CreateLeagueTeams();
        var selectedTeam = teams[0];
        var league = gameSessionService.CreatePremierLeague(teams);

        var fixture = gameSessionService.FindNextFixtureForTeam(league, selectedTeam);

        Assert.True(fixture.HomeTeam == selectedTeam || fixture.AwayTeam == selectedTeam);
        Assert.False(fixture.IsPlayed);
    }

    [Fact]
    public void SimulateSelectedTeamFixture_UpdatesFixtureAndLeagueTable()
    {
        var seedDataService = new LeagueSeedDataService();
        var gameSessionService = new GameSessionService();
        var teams = seedDataService.CreateLeagueTeams();
        var selectedTeam = teams[0];
        var league = gameSessionService.CreatePremierLeague(teams);
        var fixture = gameSessionService.FindNextFixtureForTeam(league, selectedTeam);

        var match = gameSessionService.SimulateSelectedTeamFixture(league, selectedTeam);

        Assert.True(fixture.IsPlayed);
        Assert.Same(match, fixture.Result);
        Assert.All(league.Fixtures.Where(roundFixture => roundFixture.RoundNumber == fixture.RoundNumber), roundFixture =>
        {
            Assert.True(roundFixture.IsPlayed);
            Assert.NotNull(roundFixture.Result);
        });
        Assert.Equal(4, league.Table.Sum(entry => entry.Played));
        Assert.Equal(
            league.Fixtures
                .Where(roundFixture => roundFixture.RoundNumber == fixture.RoundNumber)
                .Sum(roundFixture => roundFixture.Result!.HomeScore + roundFixture.Result.AwayScore),
            league.Table.Sum(entry => entry.GoalsFor));
    }

    [Fact]
    public void SimulateSelectedTeamSecondHalf_CompletesFixtureAndUpdatesTable()
    {
        var seedDataService = new LeagueSeedDataService();
        var gameSessionService = new GameSessionService();
        var teams = seedDataService.CreateLeagueTeams();
        var selectedTeam = teams[0];
        var league = gameSessionService.CreatePremierLeague(teams);
        var fixture = gameSessionService.FindNextFixtureForTeam(league, selectedTeam);

        var firstHalf = gameSessionService.SimulateSelectedTeamFirstHalf(league, selectedTeam);
        selectedTeam.Formation = "3-5-2";
        var finalResult = gameSessionService.SimulateSelectedTeamSecondHalf(league, fixture, firstHalf);

        Assert.True(fixture.IsPlayed);
        Assert.Same(finalResult, fixture.Result);
        Assert.All(league.Fixtures.Where(roundFixture => roundFixture.RoundNumber == fixture.RoundNumber), roundFixture =>
        {
            Assert.True(roundFixture.IsPlayed);
            Assert.NotNull(roundFixture.Result);
        });
        Assert.Equal(4, league.Table.Sum(entry => entry.Played));
        Assert.Equal(
            league.Fixtures
                .Where(roundFixture => roundFixture.RoundNumber == fixture.RoundNumber)
                .Sum(roundFixture => roundFixture.Result!.HomeScore + roundFixture.Result.AwayScore),
            league.Table.Sum(entry => entry.GoalsFor));
    }
}
