using FootballSimulation.Data;
using FootballSimulation.Engine;
using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class SeasonRolloverServiceTests
{
    [Fact]
    public void SaveGame_AndLoadGame_PreservesSeasonHistory()
    {
        var saveDirectory = CreateTempSaveDirectory();
        var saveGameService = new SaveGameService(saveDirectory);
        var teams = new LeagueSeedDataService().CreateLeagueTeams().Take(4).ToList();
        var league = new GameSessionService().CreatePremierLeague(teams);
        var selectedTeam = teams[0];
        league.SeasonHistory.Add(new SeasonArchive
        {
            Season = "2024-25",
            LeagueId = league.LeagueId,
            LeagueName = league.Name,
            SelectedClubName = selectedTeam.Name,
            SelectedClubPosition = 2,
            SelectedClubOutcome = "Qualified for Champions League",
            FinalTable =
            [
                new ArchivedLeagueTableRow { Position = 1, TeamName = teams[1].Name, Points = 82 },
                new ArchivedLeagueTableRow { Position = 2, TeamName = selectedTeam.Name, Points = 78 }
            ],
            Awards = new SeasonAwards
            {
                PlayerOfTheSeason = new SeasonAwardWinner
                {
                    AwardName = "Player of the Season",
                    PlayerName = selectedTeam.Players[0].Name,
                    TeamName = selectedTeam.Name
                }
            }
        });

        try
        {
            saveGameService.SaveGame(1, SaveGameService.CreateSaveData(league, selectedTeam));

            var saveSlot = saveGameService.GetSaveSlots().Single(slot => slot.SlotNumber == 1);
            var loadedData = saveGameService.LoadGame(1);
            var loadedLeague = SaveGameService.CreateLeague(loadedData!);

            Assert.Equal("2025-26", saveSlot.Season);
            Assert.NotNull(loadedData);
            Assert.Single(loadedData!.SeasonHistory);
            Assert.Single(loadedLeague.SeasonHistory);
            Assert.Equal("2024-25", loadedLeague.SeasonHistory[0].Season);
            Assert.Equal(selectedTeam.Name, loadedLeague.SeasonHistory[0].SelectedClubName);
            Assert.Equal("Player of the Season", loadedLeague.SeasonHistory[0].Awards.PlayerOfTheSeason.AwardName);
        }
        finally
        {
            DeleteDirectory(saveDirectory);
        }
    }

    [Fact]
    public void StartNextSeason_ArchivesAndResetsCompletedLeague()
    {
        var leagueEngine = new LeagueEngine();
        var dataService = new LeagueDataService();
        var definition = dataService.GetLeagueDefinition("premier-league");
        var teams = dataService.LoadTeams(definition).Take(6).ToList();
        var selectedTeam = teams[0];
        var league = leagueEngine.CreateLeague("premier-league", GameSessionService.PremierLeagueName, "2025-26", teams);
        SimulateAllFixtures(leagueEngine, league);

        var transferMarket = new TransferMarketService().CreateInitialState(league);
        var result = new SeasonRolloverService().StartNextSeason(league, selectedTeam, transferMarket);

        Assert.Equal("2026-27", result.League.Season);
        Assert.Single(result.League.SeasonHistory);
        Assert.Equal("2025-26", result.Archive.Season);
        Assert.NotEmpty(result.Archive.FinalTable);
        Assert.NotEmpty(result.Archive.PlayerStats);
        Assert.NotEmpty(result.Archive.Awards.BestXi);
        Assert.Equal(6, result.League.Teams.Count);
        Assert.Equal(3, result.PromotedClubs.Count);
        Assert.All(result.PromotedClubs, club =>
        {
            Assert.True(club.Players.Concat(club.Substitutes).Count() >= 18);
            Assert.Contains(club.Players, player => player.Position == Position.Goalkeeper);
        });
        Assert.All(result.League.Fixtures, fixture => Assert.False(fixture.IsPlayed));
        Assert.All(result.League.Table, row =>
        {
            Assert.Equal(0, row.Played);
            Assert.Equal(0, row.Points);
        });
        Assert.Empty(result.League.PlayerStats);
        Assert.Empty(result.League.PlayerCompetitionStats);
        Assert.Contains(result.League.Fixtures, fixture => fixture.Competition == CompetitionType.FACup);
        Assert.Contains(result.League.Fixtures, fixture => fixture.Competition == CompetitionType.LeagueCup);
        Assert.Contains(result.League.Fixtures, fixture => fixture.Competition == CompetitionType.ChampionsLeague);
        var expectedUclLeagueTeams = result.Archive.FinalTable
            .OrderBy(row => row.Position)
            .Take(4)
            .Where(row => result.League.Teams.Any(team => team.Name.Equals(row.TeamName, StringComparison.OrdinalIgnoreCase)))
            .Select(row => row.TeamName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actualUclLeagueTeams = result.League.CompetitionStates
            .Single(state => state.Competition == CompetitionType.ChampionsLeague)
            .QualifiedTeamNames
            .Where(teamName => result.League.Teams.Any(team => team.Name.Equals(teamName, StringComparison.OrdinalIgnoreCase)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.True(
            expectedUclLeagueTeams.SetEquals(actualUclLeagueTeams),
            $"Expected UCL league teams: {string.Join(", ", expectedUclLeagueTeams)}. Actual: {string.Join(", ", actualUclLeagueTeams)}.");
        Assert.Equal("2026-27", result.TransferMarketState.ActiveSeason);
        Assert.Contains(result.League.Teams, team => team.Name == result.SelectedTeam.Name);
        Assert.True(result.Archive.BudgetSummary.NewBudget > 0);
    }

    [Fact]
    public void StartNextSeason_UsesLastSeasonTopFourForChampionsLeague()
    {
        var leagueEngine = new LeagueEngine();
        var dataService = new LeagueDataService();
        var definition = dataService.GetLeagueDefinition("premier-league");
        var teams = dataService.LoadTeams(definition).Take(8).ToList();
        var selectedTeam = teams[0];
        var league = leagueEngine.CreateLeague("premier-league", GameSessionService.PremierLeagueName, "2025-26", teams);
        league.Table = teams
            .Select((team, index) => new LeagueTableEntry
            {
                TeamName = team.Name,
                Played = 14,
                Wins = Math.Max(0, 7 - index),
                Draws = 0,
                Losses = index,
                GoalsFor = 40 - index,
                GoalsAgainst = 10 + index,
                Points = (8 - index) * 3
            })
            .ToList();
        foreach (var fixture in league.Fixtures)
        {
            fixture.IsPlayed = true;
        }

        var expectedQualifiedTeamNames = league.Table
            .OrderByDescending(row => row.Points)
            .ThenByDescending(row => row.GoalDifference)
            .Take(4)
            .Select(row => row.TeamName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = new SeasonRolloverService().StartNextSeason(
            league,
            selectedTeam,
            new TransferMarketService().CreateInitialState(league));

        var uclState = result.League.CompetitionStates.Single(state => state.Competition == CompetitionType.ChampionsLeague);
        var actualQualifiedLeagueTeamNames = uclState.QualifiedTeamNames
            .Where(teamName => result.League.Teams.Any(team => team.Name.Equals(teamName, StringComparison.OrdinalIgnoreCase)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nonQualifiedLeagueTeamNames = result.League.Teams
            .Select(team => team.Name)
            .Where(teamName => !expectedQualifiedTeamNames.Contains(teamName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.True(
            expectedQualifiedTeamNames.SetEquals(actualQualifiedLeagueTeamNames),
            $"Expected UCL league teams: {string.Join(", ", expectedQualifiedTeamNames)}. Actual: {string.Join(", ", actualQualifiedLeagueTeamNames)}.");
        Assert.DoesNotContain(uclState.QualifiedTeamNames, nonQualifiedLeagueTeamNames.Contains);
        Assert.DoesNotContain(result.League.Fixtures.Where(fixture => fixture.Competition == CompetitionType.ChampionsLeague), fixture =>
            nonQualifiedLeagueTeamNames.Contains(fixture.HomeTeam.Name) ||
            nonQualifiedLeagueTeamNames.Contains(fixture.AwayTeam.Name));
    }

    [Fact]
    public void CompleteRemainingAiFixturesIfSelectedTeamFinished_CompletesGeneratedNeutralCompetitionRounds()
    {
        var leagueEngine = new LeagueEngine();
        var dataService = new LeagueDataService();
        var definition = dataService.GetLeagueDefinition("premier-league");
        var teams = dataService.LoadTeams(definition).Take(8).ToList();
        var selectedTeam = teams[0];
        var league = leagueEngine.CreateLeague("premier-league", GameSessionService.PremierLeagueName, "2025-26", teams);

        var safety = 0;
        while (league.Fixtures.Any(fixture => !fixture.IsPlayed &&
            (fixture.HomeTeam.Name == selectedTeam.Name || fixture.AwayTeam.Name == selectedTeam.Name)) &&
            safety++ < 100)
        {
            var fixture = league.Fixtures
                .Where(fixture => !fixture.IsPlayed &&
                    (fixture.HomeTeam.Name == selectedTeam.Name || fixture.AwayTeam.Name == selectedTeam.Name))
                .OrderBy(fixture => fixture.CalendarRound > 0 ? fixture.CalendarRound : fixture.RoundNumber)
                .ThenBy(fixture => fixture.Competition)
                .First();
            leagueEngine.SimulateFixture(league, fixture, options: new MatchSimulationOptions
            {
                EnableInjuries = false,
                EnableDynamicFatigue = false
            });
        }

        var service = new SeasonRolloverService();
        service.CompleteRemainingAiFixturesIfSelectedTeamFinished(league, selectedTeam);
        var archive = new SeasonAwardsService().CreateArchive(league, selectedTeam);

        Assert.DoesNotContain(league.Fixtures, fixture => !fixture.IsPlayed);
        Assert.True(league.IsCompleted);
        Assert.All(Enum.GetValues<CompetitionType>(), competition =>
        {
            var result = archive.CompetitionResults.Single(item => item.Competition == competition);
            Assert.False(string.IsNullOrWhiteSpace(result.WinnerTeamName), $"{competition} should have a winner.");
        });
        Assert.DoesNotContain(archive.Highlights, highlight =>
            highlight.Title is "Copa del Rey" or "DFB-Pokal" or "Coppa Italia" or "Coupe de France" or "Europa League" or "Conference League" &&
            highlight.PrimaryText.Equals("No winner recorded.", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(archive.Highlights, highlight =>
            highlight.Title is "Copa del Rey" or "DFB-Pokal" or "Coppa Italia" or "Coupe de France" or "Europa League" or "Conference League" &&
            highlight.SecondaryText.Contains("Did not participate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CompleteRemainingAiFixturesIfSelectedTeamFinished_RepairsOldNeutralCupWithMissingRounds()
    {
        var selectedTeam = new Team { Name = "Chelsea" };
        var teams = new[]
        {
            PlaceholderTeamFactory.Create("Real Madrid", 82),
            PlaceholderTeamFactory.Create("Barcelona", 82),
            PlaceholderTeamFactory.Create("Atletico Madrid", 80),
            PlaceholderTeamFactory.Create("Athletic Club", 79),
            PlaceholderTeamFactory.Create("Real Sociedad", 78),
            PlaceholderTeamFactory.Create("Villarreal", 78),
            PlaceholderTeamFactory.Create("Real Betis", 77),
            PlaceholderTeamFactory.Create("Sevilla", 77)
        };
        var fixtures = teams
            .Chunk(2)
            .Select(pair => new Fixture
            {
                Competition = CompetitionType.CopaDelRey,
                RoundName = "Round of 16",
                CalendarRound = 25,
                RoundNumber = 25,
                IsKnockout = true,
                IsPlayed = true,
                HomeTeam = pair[0],
                AwayTeam = pair[1],
                Result = new Match
                {
                    HomeTeam = pair[0],
                    AwayTeam = pair[1],
                    HomeScore = 2,
                    AwayScore = 0,
                    CurrentPhase = MatchPhase.Fulltime,
                    CurrentMinute = 90
                },
                WinningTeamName = pair[0].Name,
                LosingTeamName = pair[1].Name
            })
            .ToList();
        var league = new League
        {
            Season = "2025-26",
            Teams = [selectedTeam],
            Table = [new LeagueTableEntry { TeamName = selectedTeam.Name, Played = 38, Wins = 38, Points = 114 }],
            Fixtures = fixtures,
            CompetitionStates =
            [
                new SeasonCompetitionState
                {
                    Competition = CompetitionType.CopaDelRey,
                    Name = "Copa del Rey",
                    CurrentRoundName = "Complete",
                    IsActive = false
                }
            ]
        };

        new SeasonRolloverService().CompleteRemainingAiFixturesIfSelectedTeamFinished(league, selectedTeam);

        var result = new SeasonAwardsService()
            .CreateArchive(league, selectedTeam)
            .CompetitionResults
            .Single(result => result.Competition == CompetitionType.CopaDelRey);
        Assert.False(string.IsNullOrWhiteSpace(result.WinnerTeamName));
        Assert.DoesNotContain(league.Fixtures, fixture => fixture.Competition == CompetitionType.CopaDelRey && !fixture.IsPlayed);
    }

    [Fact]
    public void ApplySeasonRolloverBudget_UsesCarryoverAndBonuses()
    {
        var team = new Team
        {
            Name = "Budget FC",
            Players =
            [
                new Player { Name = "Player One", OverallRating = 80, WeeklyWage = 50_000 },
                new Player { Name = "Player Two", OverallRating = 78, WeeklyWage = 40_000 }
            ]
        };
        var state = new TransferMarketState
        {
            ClubFinances =
            [
                new ClubFinance
                {
                    LeagueId = "test",
                    ClubName = team.Name,
                    ClubTransferBudget = 100_000_000m,
                    TransferSpent = 20_000_000m,
                    TransferIncome = 10_000_000m
                }
            ]
        };
        var expectedCarryover = 45_000_000m;
        var expectedBase = ClubFinanceService.GetBaseBudget(team.Name, 79);

        var summary = new ClubFinanceService().ApplySeasonRolloverBudget(state, "test", team, finalPosition: 1, teamCount: 20);

        Assert.Equal(expectedCarryover, summary.RemainingCarryover);
        Assert.Equal(expectedBase, summary.BaseBudget);
        Assert.Equal(60_000_000m, summary.PerformanceBonus);
        Assert.Equal(40_000_000m, summary.QualificationBonus);
        Assert.Equal(expectedCarryover + expectedBase + 100_000_000m, summary.NewBudget);
        Assert.Equal(summary.NewBudget, state.ClubFinances[0].ClubTransferBudget);
        Assert.Equal(0, state.ClubFinances[0].TransferSpent);
        Assert.Equal(0, state.ClubFinances[0].TransferIncome);
        Assert.Equal(90_000m, state.ClubFinances[0].WageSpent);
    }

    private static string CreateTempSaveDirectory()
    {
        return Path.Combine(Path.GetTempPath(), $"football-save-tests-{Guid.NewGuid():N}");
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void SimulateAllFixtures(LeagueEngine leagueEngine, League league)
    {
        var safety = 0;
        while (league.Fixtures.Any(fixture => !fixture.IsPlayed) && safety++ < 500)
        {
            var fixture = league.Fixtures
                .Where(fixture => !fixture.IsPlayed)
                .OrderBy(fixture => fixture.CalendarRound > 0 ? fixture.CalendarRound : fixture.RoundNumber)
                .ThenBy(fixture => fixture.Competition)
                .First();
            leagueEngine.SimulateFixture(league, fixture, seed: 12 + safety, options: new MatchSimulationOptions
            {
                EnableInjuries = false,
                EnableDynamicFatigue = false
            });
        }

        Assert.DoesNotContain(league.Fixtures, fixture => !fixture.IsPlayed);
    }
}
