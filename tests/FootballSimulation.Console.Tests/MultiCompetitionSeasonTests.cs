using FootballSimulation.Data;
using FootballSimulation.Engine;
using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class MultiCompetitionSeasonTests
{
    [Fact]
    public void SeasonCalendar_IncludesAllFourCompetitions()
    {
        var league = CreateLeague(teamCount: 8);

        Assert.Contains(league.Fixtures, fixture => fixture.Competition == CompetitionType.PremierLeague && fixture.AffectsLeagueTable);
        Assert.Contains(league.Fixtures, fixture => fixture.Competition == CompetitionType.FACup && fixture.IsKnockout);
        Assert.Contains(league.Fixtures, fixture => fixture.Competition == CompetitionType.LeagueCup && fixture.IsKnockout);
        Assert.Contains(league.Fixtures, fixture => fixture.Competition == CompetitionType.ChampionsLeague && !fixture.AffectsLeagueTable);
        Assert.All(league.Fixtures, fixture => Assert.True(fixture.CalendarRound > 0));
    }

    [Fact]
    public void NextFixture_UsesCalendarOrderAcrossCompetitions()
    {
        var league = CreateLeague(teamCount: 8);
        var selectedTeam = league.Teams[0];
        var expected = league.Fixtures
            .Where(fixture => IsTeamInFixture(fixture, selectedTeam))
            .OrderBy(fixture => fixture.CalendarRound)
            .ThenBy(fixture => fixture.Competition)
            .First();

        var actual = new GameSessionService().FindNextFixtureForTeam(league, selectedTeam);

        Assert.Same(expected, actual);
    }

    [Fact]
    public void Progression_OnlyPremierLeagueFixturesUpdateLeagueTable()
    {
        var engine = new LeagueEngine();
        var league = CreateLeague(teamCount: 8, engine);
        var faCupFixture = league.Fixtures.First(fixture => fixture.Competition == CompetitionType.FACup);
        var premierLeagueFixture = league.Fixtures.First(fixture => fixture.Competition == CompetitionType.PremierLeague);

        engine.SimulateFixture(league, faCupFixture, seed: 41);

        Assert.All(league.Table, row => Assert.Equal(0, row.Played));

        engine.SimulateFixture(league, premierLeagueFixture, seed: 42);

        Assert.Equal(2, league.Table.Sum(row => row.Played));
    }

    [Fact]
    public void CupProgression_GeneratesNextRoundWhenRoundCompletes()
    {
        var progression = new CompetitionProgressionService();
        var league = CreateLeague(teamCount: 8);
        var thirdRoundFixtures = league.Fixtures
            .Where(fixture => fixture.Competition == CompetitionType.LeagueCup && fixture.RoundName == "Third Round")
            .ToList();

        foreach (var fixture in thirdRoundFixtures)
        {
            CompleteFixture(progression, league, fixture, homeScore: 2, awayScore: 1);
        }

        Assert.Contains(league.Fixtures, fixture =>
            fixture.Competition == CompetitionType.LeagueCup &&
            fixture.RoundName == "Fourth Round" &&
            !fixture.IsPlayed);
        Assert.All(thirdRoundFixtures, fixture => Assert.False(string.IsNullOrWhiteSpace(fixture.WinningTeamName)));
    }

    [Fact]
    public void ChampionsLeague_GroupCompletionCreatesRoundOf16()
    {
        var progression = new CompetitionProgressionService();
        var league = CreateLeague(teamCount: 8);
        var groupFixtures = league.Fixtures
            .Where(fixture => fixture.Competition == CompetitionType.ChampionsLeague && !fixture.IsKnockout)
            .ToList();

        foreach (var fixture in groupFixtures)
        {
            CompleteFixture(progression, league, fixture, homeScore: 1, awayScore: 0);
        }

        Assert.Contains(league.Fixtures, fixture =>
            fixture.Competition == CompetitionType.ChampionsLeague &&
            fixture.RoundName == "Round of 16" &&
            fixture.IsKnockout);
        Assert.Equal(8, league.CompetitionStates
            .First(state => state.Competition == CompetitionType.ChampionsLeague)
            .ChampionsLeagueGroups.Count);
    }

    [Fact]
    public void SaveLoad_PreservesCompetitionFixturesStateAndStats()
    {
        var saveDirectory = Path.Combine(Path.GetTempPath(), $"football-multi-competition-{Guid.NewGuid():N}");
        var saveGameService = new SaveGameService(saveDirectory);
        var engine = new LeagueEngine();
        var league = CreateLeague(teamCount: 8, engine);
        var selectedTeam = league.Teams[0];
        engine.SimulateFixture(league, league.Fixtures.First(fixture => fixture.Competition == CompetitionType.PremierLeague), seed: 101);
        engine.SimulateFixture(league, league.Fixtures.First(fixture => fixture.Competition == CompetitionType.FACup), seed: 102);

        try
        {
            saveGameService.SaveGame(1, SaveGameService.CreateSaveData(league, selectedTeam));

            var loadedData = saveGameService.LoadGame(1)!;
            var loadedLeague = SaveGameService.CreateLeague(loadedData);

            Assert.Contains(loadedLeague.Fixtures, fixture => fixture.Competition == CompetitionType.FACup);
            Assert.Contains(loadedLeague.Fixtures, fixture => fixture.Competition == CompetitionType.ChampionsLeague);
            Assert.NotEmpty(loadedLeague.CompetitionStates);
            Assert.NotEmpty(loadedLeague.PlayerCompetitionStats);
        }
        finally
        {
            if (Directory.Exists(saveDirectory))
            {
                Directory.Delete(saveDirectory, recursive: true);
            }
        }
    }

    private static League CreateLeague(int teamCount, LeagueEngine? engine = null)
    {
        var dataService = new LeagueDataService();
        var definition = dataService.GetLeagueDefinition("premier-league");
        var teams = dataService.LoadTeams(definition).Take(teamCount).ToList();
        return (engine ?? new LeagueEngine()).CreateLeague("premier-league", GameSessionService.PremierLeagueName, "2025-26", teams);
    }

    private static void CompleteFixture(
        CompetitionProgressionService progression,
        League league,
        Fixture fixture,
        int homeScore,
        int awayScore)
    {
        fixture.Result = new Match
        {
            HomeTeam = fixture.HomeTeam,
            AwayTeam = fixture.AwayTeam,
            HomeScore = homeScore,
            AwayScore = awayScore,
            CurrentPhase = MatchPhase.Fulltime,
            CurrentMinute = 90
        };
        fixture.IsPlayed = true;
        progression.ProcessCompletedFixture(league, fixture, seed: 7);
    }

    private static bool IsTeamInFixture(Fixture fixture, Team team)
    {
        return fixture.HomeTeam.Name.Equals(team.Name, StringComparison.OrdinalIgnoreCase) ||
            fixture.AwayTeam.Name.Equals(team.Name, StringComparison.OrdinalIgnoreCase);
    }
}
