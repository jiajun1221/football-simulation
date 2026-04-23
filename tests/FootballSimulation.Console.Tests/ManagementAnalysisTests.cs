using FootballSimulation.Data;
using FootballSimulation.Engine;
using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class ManagementAnalysisTests
{
    [Fact]
    public void SimulateMatch_TracksPlayerPerformancesAndExpectedGoals()
    {
        var seedDataService = new SeedDataService();
        var (homeTeam, awayTeam) = seedDataService.CreateDemoTeams();
        var engine = new MatchEngine();

        var match = engine.SimulateMatch(homeTeam, awayTeam, seed: 42);

        Assert.NotEmpty(match.PlayerPerformances);
        Assert.All(match.PlayerPerformances, performance => Assert.InRange(performance.Rating, 1.0, 10.0));
        Assert.True(match.HomeStats.ExpectedGoals >= 0);
        Assert.True(match.AwayStats.ExpectedGoals >= 0);
    }

    [Fact]
    public void PostMatchAnalysisService_SelectsManOfTheMatchFromPerformance()
    {
        var match = new Match
        {
            PlayerPerformances =
            [
                new PlayerMatchPerformance { PlayerName = "Starter", TeamName = "Home", Rating = 7.0 },
                new PlayerMatchPerformance { PlayerName = "Match Winner", TeamName = "Away", Rating = 8.4, Goals = 2 }
            ]
        };
        var service = new PostMatchAnalysisService();

        var summary = service.CreateSummary(match);

        Assert.NotNull(summary.ManOfTheMatch);
        Assert.Equal("Match Winner", summary.ManOfTheMatch.PlayerName);
        Assert.Contains("goal", summary.ManOfTheMatchReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RecentResultService_ReturnsOnlyPlayedFixturesForSelectedTeam()
    {
        var seedDataService = new SeedDataService();
        var teams = new List<Team> { seedDataService.CreateHomeTeam(), seedDataService.CreateAwayTeam() };
        var leagueEngine = new LeagueEngine();
        var league = leagueEngine.CreateLeague("Test League", teams);
        var selectedTeam = teams[0];

        var fixture = league.Fixtures.First(fixture => fixture.HomeTeam == selectedTeam || fixture.AwayTeam == selectedTeam);
        leagueEngine.SimulateFixture(league, fixture, seed: 42);

        var service = new RecentResultService();
        var results = service.GetRecentResults(league, selectedTeam);

        Assert.Single(results);
        Assert.Equal(fixture.RoundNumber, results[0].RoundNumber);
        Assert.Contains(results[0].ResultType, new[] { "W", "D", "L" });
    }

    [Fact]
    public void ClubNewsService_ReturnsContextItemsWithoutPlayedFixtures()
    {
        var seedDataService = new SeedDataService();
        var teams = new List<Team> { seedDataService.CreateHomeTeam(), seedDataService.CreateAwayTeam() };
        var league = new LeagueEngine().CreateLeague("Test League", teams);
        var service = new ClubNewsService();

        var items = service.GenerateClubNews(league, teams[0]);

        Assert.Contains(items, item => item.Type == ClubNewsType.News);
        Assert.Contains(items, item => item.Type == ClubNewsType.Rumour);
        Assert.Contains(items, item => item.Type == ClubNewsType.MediaComment);
    }
}
