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

            var loadedData = saveGameService.LoadGame(1);
            var loadedLeague = SaveGameService.CreateLeague(loadedData!);

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
        Assert.Equal("2026-27", result.TransferMarketState.ActiveSeason);
        Assert.Contains(result.League.Teams, team => team.Name == result.SelectedTeam.Name);
        Assert.True(result.Archive.BudgetSummary.NewBudget > 0);
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
            leagueEngine.SimulateFixture(league, fixture, seed: 12 + safety);
        }

        Assert.DoesNotContain(league.Fixtures, fixture => !fixture.IsPlayed);
    }
}
