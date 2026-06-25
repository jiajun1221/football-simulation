using FootballSimulation.Data;
using FootballSimulation.Engine;
using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class MultiCompetitionSeasonTests
{
    [Fact]
    public void SeasonCalendar_IncludesAllTrackedCompetitions()
    {
        var league = CreateLeague(teamCount: 20);

        Assert.Contains(league.Fixtures, fixture => fixture.Competition == CompetitionType.PremierLeague && fixture.AffectsLeagueTable);
        Assert.Contains(league.Fixtures, fixture => fixture.Competition == CompetitionType.FACup && fixture.IsKnockout);
        Assert.Contains(league.Fixtures, fixture => fixture.Competition == CompetitionType.LeagueCup && fixture.IsKnockout);
        Assert.Contains(league.Fixtures, fixture => fixture.Competition == CompetitionType.ChampionsLeague && !fixture.AffectsLeagueTable);
        Assert.Contains(league.Fixtures, fixture => fixture.Competition == CompetitionType.CopaDelRey && fixture.IsKnockout);
        Assert.Contains(league.Fixtures, fixture => fixture.Competition == CompetitionType.DfbPokal && fixture.IsKnockout);
        Assert.Contains(league.Fixtures, fixture => fixture.Competition == CompetitionType.CoppaItalia && fixture.IsKnockout);
        Assert.Contains(league.Fixtures, fixture => fixture.Competition == CompetitionType.CoupeDeFrance && fixture.IsKnockout);
        Assert.Contains(league.Fixtures, fixture => fixture.Competition == CompetitionType.EuropaLeague && fixture.IsKnockout);
        Assert.Contains(league.Fixtures, fixture => fixture.Competition == CompetitionType.ConferenceLeague && fixture.IsKnockout);
        Assert.All(league.Fixtures, fixture => Assert.True(fixture.CalendarRound > 0));
    }

    [Fact]
    public void NextFixture_UsesCalendarOrderAcrossCompetitions()
    {
        var league = CreateLeague(teamCount: 20);
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
    public void ChampionsLeague_LeaguePhaseUsesSwissStyleEuropeanOpponents()
    {
        var league = CreateLeague(teamCount: 20);
        var selectedTeam = league.Teams.First(team => team.Name == "Chelsea");
        var uclFixtures = league.Fixtures
            .Where(fixture => fixture.Competition == CompetitionType.ChampionsLeague && !fixture.IsKnockout)
            .ToList();
        var selectedFixtures = uclFixtures
            .Where(fixture => IsTeamInFixture(fixture, selectedTeam))
            .ToList();
        var opponentNames = selectedFixtures
            .Select(fixture => fixture.HomeTeam.Name == selectedTeam.Name ? fixture.AwayTeam.Name : fixture.HomeTeam.Name)
            .ToList();
        var englishOpponents = opponentNames.Count(name => name is "Arsenal" or "Manchester City" or "Liverpool" or "Manchester United" or "Tottenham Hotspur");

        Assert.Equal(36, league.CompetitionStates.First(state => state.Competition == CompetitionType.ChampionsLeague).Standings.Count);
        Assert.All(league.CompetitionStates.First(state => state.Competition == CompetitionType.ChampionsLeague).Standings, row =>
        {
            Assert.Equal(8, uclFixtures.Count(fixture => fixture.HomeTeam.Name == row.TeamName || fixture.AwayTeam.Name == row.TeamName));
        });
        Assert.Equal(8, selectedFixtures.Count);
        Assert.Equal(4, selectedFixtures.Count(fixture => fixture.HomeTeam.Name == selectedTeam.Name));
        Assert.Equal(4, selectedFixtures.Count(fixture => fixture.AwayTeam.Name == selectedTeam.Name));
        Assert.Equal(8, opponentNames.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.True(englishOpponents <= 1, $"Expected at most one English UCL opponent but found: {string.Join(", ", opponentNames)}");
        Assert.All(selectedFixtures, fixture =>
        {
            Assert.StartsWith("League Phase MD", fixture.RoundName);
            Assert.False(fixture.AffectsLeagueTable);
        });
    }

    [Fact]
    public void ChampionsLeague_LeaguePhaseSchedulesEveryClubOncePerMatchday()
    {
        var league = CreateLeague(teamCount: 20);
        var uclTeamNames = league.CompetitionStates
            .First(state => state.Competition == CompetitionType.ChampionsLeague)
            .Standings
            .Select(row => row.TeamName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fixturesByMatchday = league.Fixtures
            .Where(fixture => fixture.Competition == CompetitionType.ChampionsLeague && !fixture.IsKnockout)
            .GroupBy(fixture => fixture.RoundNumber)
            .ToList();

        Assert.Equal(8, fixturesByMatchday.Count);
        Assert.All(fixturesByMatchday, matchday =>
        {
            var matchdayTeamNames = matchday
                .SelectMany(fixture => new[] { fixture.HomeTeam.Name, fixture.AwayTeam.Name })
                .ToList();

            Assert.Equal(18, matchday.Count());
            Assert.Equal(uclTeamNames.Count, matchdayTeamNames.Count);
            Assert.Equal(uclTeamNames.Count, matchdayTeamNames.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.True(
                uclTeamNames.SetEquals(matchdayTeamNames),
                $"Matchday {matchday.Key} does not include every UCL club exactly once.");
        });
    }

    [Fact]
    public void ChampionsLeague_CompletedUserMatchdayLevelsPlayedCountForEveryClub()
    {
        var engine = new LeagueEngine();
        var league = CreateLeague(teamCount: 20, engine);
        var selectedFixture = league.Fixtures.First(fixture =>
            fixture.Competition == CompetitionType.ChampionsLeague &&
            !fixture.IsKnockout &&
            fixture.RoundNumber == 1);

        engine.SimulateFixture(league, selectedFixture, seed: 27);
        engine.SimulateRemainingFixturesForCompetitionRound(league, selectedFixture, seed: 28);

        var uclState = league.CompetitionStates.First(state => state.Competition == CompetitionType.ChampionsLeague);
        Assert.All(uclState.Standings, row => Assert.Equal(1, row.Played));
        Assert.DoesNotContain(league.Fixtures.Where(fixture =>
            fixture.Competition == CompetitionType.ChampionsLeague &&
            !fixture.IsKnockout &&
            fixture.RoundNumber == selectedFixture.RoundNumber), fixture => !fixture.IsPlayed);
    }

    [Fact]
    public void ChampionsLeague_LeaguePhaseUsesRealSquadNamesForAllEntrants()
    {
        var league = CreateLeague(teamCount: 20);
        var uclTeams = league.Fixtures
            .Where(fixture => fixture.Competition == CompetitionType.ChampionsLeague)
            .SelectMany(fixture => new[] { fixture.HomeTeam, fixture.AwayTeam })
            .GroupBy(team => team.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        Assert.Equal(36, uclTeams.Count);
        Assert.All(uclTeams, team =>
        {
            Assert.Equal(11, team.Players.Count);
            Assert.InRange(team.Substitutes.Count, 7, 12);
            Assert.DoesNotContain(team.Players.Concat(team.Substitutes), player =>
                player.Name.Contains(" Player ", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(team.Players.Concat(team.Substitutes), player =>
                player.PlayerId.StartsWith("placeholder-", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void ChampionsLeague_LeaguePhaseCompletionCreatesRoundOf16()
    {
        var progression = new CompetitionProgressionService();
        var league = CreateLeague(teamCount: 8);
        var leaguePhaseFixtures = league.Fixtures
            .Where(fixture => fixture.Competition == CompetitionType.ChampionsLeague && !fixture.IsKnockout)
            .ToList();

        foreach (var fixture in leaguePhaseFixtures)
        {
            CompleteFixture(progression, league, fixture, homeScore: 1, awayScore: 0);
        }

        Assert.Contains(league.Fixtures, fixture =>
            fixture.Competition == CompetitionType.ChampionsLeague &&
            fixture.RoundName == "Round of 16" &&
            fixture.IsKnockout);
        var state = league.CompetitionStates.First(state => state.Competition == CompetitionType.ChampionsLeague);
        Assert.Equal(36, state.Standings.Count);
        Assert.Equal("Round of 16", state.CurrentRoundName);
        Assert.NotEmpty(state.ProgressRecords);
    }

    [Fact]
    public void ChampionsLeague_QuarterFinalIsKnockoutImportanceNotFinal()
    {
        var progression = new CompetitionProgressionService();
        var league = CreateLeague(teamCount: 8);
        var leaguePhaseFixtures = league.Fixtures
            .Where(fixture => fixture.Competition == CompetitionType.ChampionsLeague && !fixture.IsKnockout)
            .ToList();

        foreach (var fixture in leaguePhaseFixtures)
        {
            CompleteFixture(progression, league, fixture, homeScore: 1, awayScore: 0);
        }

        var roundOf16Fixtures = league.Fixtures
            .Where(fixture => fixture.Competition == CompetitionType.ChampionsLeague && fixture.RoundName == "Round of 16")
            .ToList();
        foreach (var fixture in roundOf16Fixtures)
        {
            CompleteFixture(progression, league, fixture, homeScore: 2, awayScore: 1);
        }

        var quarterFinalFixtures = league.Fixtures
            .Where(fixture => fixture.Competition == CompetitionType.ChampionsLeague && fixture.RoundName == "Quarter Final")
            .ToList();

        Assert.NotEmpty(quarterFinalFixtures);
        Assert.All(quarterFinalFixtures, fixture => Assert.Equal(FixtureImportance.Knockout, fixture.Importance));
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
