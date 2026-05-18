using FootballSimulation.Engine;
using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class PlayerSeasonStatsServiceTests
{
    [Fact]
    public void RebuildSeasonStats_AggregatesCompletedFixturePlayerStats()
    {
        var homeGoalkeeper = CreatePlayer("Home Keeper", Position.Goalkeeper, 1);
        var homeScorer = CreatePlayer("Home Scorer", Position.Forward, 9);
        var homeCreator = CreatePlayer("Home Creator", Position.Midfielder, 10);
        var awayGoalkeeper = CreatePlayer("Away Keeper", Position.Goalkeeper, 1);
        var awayDefender = CreatePlayer("Away Defender", Position.Defender, 5);
        var homeTeam = CreateTeam("Home FC", [homeGoalkeeper, homeScorer, homeCreator]);
        var awayTeam = CreateTeam("Away FC", [awayGoalkeeper, awayDefender, CreatePlayer("Away Striker", Position.Forward, 9)]);
        var match = new Match
        {
            HomeTeam = homeTeam,
            AwayTeam = awayTeam,
            HomeScore = 2,
            AwayScore = 0,
            PlayerPerformances =
            [
                new() { PlayerName = homeGoalkeeper.Name, TeamName = homeTeam.Name, Position = Position.Goalkeeper, Rating = 7.4 },
                new() { PlayerName = homeScorer.Name, TeamName = homeTeam.Name, Position = Position.Forward, Goals = 2, Rating = 8.6 },
                new() { PlayerName = homeCreator.Name, TeamName = homeTeam.Name, Position = Position.Midfielder, Assists = 1, YellowCards = 1, Rating = 7.7 },
                new() { PlayerName = awayGoalkeeper.Name, TeamName = awayTeam.Name, Position = Position.Goalkeeper, Saves = 5, Rating = 7.2 },
                new() { PlayerName = awayDefender.Name, TeamName = awayTeam.Name, Position = Position.Defender, RedCards = 1, Rating = 5.4 }
            ]
        };
        var league = CreateLeague(homeTeam, awayTeam, match);

        var stats = new PlayerSeasonStatsService().RebuildSeasonStats(league);

        var scorerStats = stats.Single(stat => stat.PlayerName == homeScorer.Name);
        Assert.Equal(1, scorerStats.Appearances);
        Assert.Equal(2, scorerStats.Goals);
        Assert.Equal("ST", scorerStats.ExactPosition);
        Assert.Equal(8.6, scorerStats.AverageRating);

        var keeperStats = stats.Single(stat => stat.PlayerName == homeGoalkeeper.Name);
        Assert.Equal(1, keeperStats.CleanSheets);
        Assert.Equal(0, keeperStats.GoalsConceded);

        var awayKeeperStats = stats.Single(stat => stat.PlayerName == awayGoalkeeper.Name);
        Assert.Equal(5, awayKeeperStats.Saves);
        Assert.Equal(2, awayKeeperStats.GoalsConceded);
    }

    [Fact]
    public void LeagueEngine_RefreshesPlayerStatsAfterFixture()
    {
        var dataService = new LeagueDataService();
        var definition = dataService.GetLeagueDefinition("la-liga");
        var teams = dataService.LoadTeams(definition).Take(4).ToList();
        var league = new GameSessionService().CreateLeague(definition, teams);
        var fixture = league.Fixtures[0];

        new LeagueEngine().SimulateFixture(league, fixture, seed: 8);

        Assert.NotEmpty(league.PlayerStats);
        Assert.All(league.PlayerStats, stat => Assert.True(stat.Appearances > 0));
        Assert.Contains(league.PlayerStats, stat => stat.Goals > 0 || stat.Saves > 0 || stat.Assists > 0);
    }

    [Fact]
    public void SaveData_RestoresPlayerStats()
    {
        var dataService = new LeagueDataService();
        var definition = dataService.GetLeagueDefinition("serie-a");
        var teams = dataService.LoadTeams(definition).Take(4).ToList();
        var league = new GameSessionService().CreateLeague(definition, teams);
        new LeagueEngine().SimulateFixture(league, league.Fixtures[0], seed: 4);

        var saveData = SaveGameService.CreateSaveData(league, league.Teams[0]);
        var restoredLeague = SaveGameService.CreateLeague(saveData);

        Assert.NotEmpty(saveData.PlayerStats);
        Assert.Equal(saveData.PlayerStats.Count, restoredLeague.PlayerStats.Count);
        Assert.Equal(
            saveData.PlayerStats.OrderBy(stat => stat.PlayerId).Select(stat => stat.PlayerId),
            restoredLeague.PlayerStats.OrderBy(stat => stat.PlayerId).Select(stat => stat.PlayerId));
    }

    private static Player CreatePlayer(string name, Position position, int squadNumber)
    {
        return new Player
        {
            Name = name,
            SquadNumber = squadNumber,
            Position = position,
            PreferredPosition = position switch
            {
                Position.Goalkeeper => "GK",
                Position.Defender => "CB",
                Position.Midfielder => "CM",
                _ => "ST"
            },
            OverallRating = 75,
            Stamina = 80,
            CurrentStamina = 80
        };
    }

    private static Team CreateTeam(string name, List<Player> players)
    {
        return new Team
        {
            Name = name,
            Players = players
        };
    }

    private static League CreateLeague(Team homeTeam, Team awayTeam, Match match)
    {
        return new League
        {
            LeagueId = "test-league",
            Name = "Test League",
            Teams = [homeTeam, awayTeam],
            Fixtures =
            [
                new Fixture
                {
                    HomeTeam = homeTeam,
                    AwayTeam = awayTeam,
                    IsPlayed = true,
                    Result = match
                }
            ]
        };
    }
}
