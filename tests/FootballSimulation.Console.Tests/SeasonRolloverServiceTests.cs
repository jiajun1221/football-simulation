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
    public void StartNextSeason_AgesCarryoverPlayersTransferSnapshotsAndFreeAgentsOnce()
    {
        var leagueEngine = new LeagueEngine();
        var dataService = new LeagueDataService();
        var definition = dataService.GetLeagueDefinition("premier-league");
        var teams = dataService.LoadTeams(definition).Take(6).ToList();
        var selectedTeam = teams[0];
        var league = leagueEngine.CreateLeague("premier-league", GameSessionService.PremierLeagueName, "2025-26", teams);
        var selectedStarter = selectedTeam.Players[0];
        var selectedSubstitute = selectedTeam.Substitutes[0];
        selectedStarter.PlayerId = "selected-starter-age-test";
        selectedSubstitute.PlayerId = "selected-substitute-age-test";
        selectedStarter.Age = 24;
        selectedSubstitute.Age = 20;
        league.Table = teams
            .Select((team, index) => new LeagueTableEntry
            {
                TeamName = team.Name,
                Played = 10,
                Wins = Math.Max(0, 5 - index),
                Draws = 0,
                Losses = index,
                GoalsFor = 30 - index,
                GoalsAgainst = 10 + index,
                Points = (6 - index) * 3
            })
            .ToList();
        foreach (var fixture in league.Fixtures)
        {
            fixture.IsPlayed = true;
        }

        var transferMarket = new TransferMarketService().CreateInitialState(league);
        var foreignStarter = new Player
        {
            PlayerId = "foreign-starter-age-test",
            Name = "Foreign Starter",
            Age = 27,
            Position = Position.Midfielder,
            PreferredPosition = "CM",
            OverallRating = 75
        };
        var foreignSubstitute = new Player
        {
            PlayerId = "foreign-substitute-age-test",
            Name = "Foreign Substitute",
            Age = 21,
            Position = Position.Forward,
            PreferredPosition = "ST",
            OverallRating = 70
        };
        var freeAgent = new Player
        {
            PlayerId = "free-agent-age-test",
            Name = "Free Agent",
            Age = 30,
            Position = Position.Defender,
            PreferredPosition = "CB",
            OverallRating = 72
        };
        transferMarket.Leagues.Add(new TransferLeagueState
        {
            LeagueId = "test-foreign",
            LeagueName = "Test Foreign League",
            Season = league.Season,
            Teams =
            [
                new Team
                {
                    Name = "Foreign FC",
                    Players = [foreignStarter],
                    Substitutes = [foreignSubstitute]
                }
            ]
        });
        transferMarket.FreeAgents.Add(freeAgent);

        var result = new SeasonRolloverService().StartNextSeason(league, selectedTeam, transferMarket);

        var agedSelectedTeam = result.League.Teams.Single(team =>
            team.Name.Equals(selectedTeam.Name, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(25, agedSelectedTeam.Players.Single(player => player.PlayerId == selectedStarter.PlayerId).Age);
        Assert.Equal(21, agedSelectedTeam.Substitutes.Single(player => player.PlayerId == selectedSubstitute.PlayerId).Age);
        Assert.Equal(28, foreignStarter.Age);
        Assert.Equal(22, foreignSubstitute.Age);
        Assert.Equal(31, freeAgent.Age);
        Assert.Contains(result.TransferMarketState.FreeAgents, player => player.PlayerId == freeAgent.PlayerId);
    }

    [Fact]
    public void StartNextSeason_RenewsValuablePlayersAndReleasesFringePlayers()
    {
        var (league, selectedTeam) = CreateCompletedRolloverContext();
        var aiTeam = league.Teams[1];
        var selectedValuablePlayer = selectedTeam.Players
            .OrderByDescending(player => player.OverallRating)
            .First();
        selectedValuablePlayer.PlayerId = "selected-valuable-renewal-test";
        selectedValuablePlayer.ContractEndYear = 2026;
        selectedValuablePlayer.Role = PlayerRole.KeyPlayer;
        var valuablePlayer = aiTeam.Players
            .OrderByDescending(player => player.OverallRating)
            .First();
        valuablePlayer.PlayerId = "valuable-renewal-test";
        valuablePlayer.ContractEndYear = 2026;
        valuablePlayer.Role = PlayerRole.Starter;
        valuablePlayer.IsStarter = true;

        var fringePlayer = new Player
        {
            PlayerId = "fringe-expiry-test",
            Name = "Fringe Expiry Test",
            ContractEndYear = 2026,
            OverallRating = 40,
            PotentialOverall = 45,
            Age = 28,
            Position = Position.Midfielder,
            PreferredPosition = "CM",
            Role = PlayerRole.Backup,
            IsStarter = false,
            IsOnPitch = false
        };
        aiTeam.Substitutes.Add(fringePlayer);

        var transferMarket = new TransferMarketState
        {
            ActiveSeason = league.Season,
            Leagues =
            [
                new TransferLeagueState
                {
                    LeagueId = league.LeagueId,
                    LeagueName = league.Name,
                    Season = league.Season,
                    Teams = league.Teams
                }
            ]
        };

        var result = new SeasonRolloverService().StartNextSeason(league, selectedTeam, transferMarket);
        var rolledOverAiTeam = result.League.Teams.Single(team => team.Name == aiTeam.Name);

        Assert.Contains(
            result.SelectedTeam.Players.Concat(result.SelectedTeam.Substitutes),
            player => player.PlayerId == selectedValuablePlayer.PlayerId);
        Assert.True(selectedValuablePlayer.ContractEndYear > 2027);
        Assert.Contains(
            rolledOverAiTeam.Players.Concat(rolledOverAiTeam.Substitutes),
            player => player.PlayerId == valuablePlayer.PlayerId);
        Assert.True(valuablePlayer.ContractEndYear > 2027);
        Assert.Equal(2026, fringePlayer.ContractEndYear);
        Assert.DoesNotContain(
            rolledOverAiTeam.Players.Concat(rolledOverAiTeam.Substitutes),
            player => player.PlayerId == fringePlayer.PlayerId);
        Assert.Contains(result.TransferMarketState.FreeAgents, player => player.PlayerId == fringePlayer.PlayerId);
    }

    [Fact]
    public void StartNextSeason_RetiresOldFreeAgentAndAddsRegenToTransferMarket()
    {
        var (league, selectedTeam) = CreateCompletedRolloverContext();
        var oldFreeAgent = new Player
        {
            PlayerId = "rollover-old-free-agent-test",
            Name = "Rollover Old Agent",
            Age = 39,
            Position = Position.Midfielder,
            PreferredPosition = "CM",
            AssignedPosition = "CM",
            SecondaryPositions = ["CAM", "CDM"],
            Nationality = "Spain",
            NationalityName = "Spain",
            NationalityCode = "ES",
            FlagImagePath = "Assets/Flags/spain.png",
            OverallRating = 84,
            BaseOverallRating = 84,
            PotentialOverall = 88,
            ContractEndYear = 2025,
            ContractStatus = PlayerContractStatus.FreeAgent
        };
        var transferMarket = new TransferMarketState
        {
            ActiveSeason = league.Season,
            FreeAgents = [oldFreeAgent]
        };

        var result = new SeasonRolloverService().StartNextSeason(league, selectedTeam, transferMarket);

        Assert.DoesNotContain(result.TransferMarketState.FreeAgents, player => player.PlayerId == oldFreeAgent.PlayerId);
        var regen = Assert.Single(result.TransferMarketState.FreeAgents, player =>
            player.PlayerId.StartsWith("regen-free-agent-2026-27-rollover-old-free-agent-test", StringComparison.OrdinalIgnoreCase));
        Assert.InRange(regen.Age.GetValueOrDefault(), 16, 19);
        Assert.Equal(Position.Midfielder, regen.Position);
        Assert.Equal("CM", regen.PreferredPosition);
        Assert.Contains("CAM", regen.SecondaryPositions);
        Assert.Equal("Spain", regen.NationalityName);
        Assert.Equal("ES", regen.NationalityCode);
        Assert.Equal(PlayerContractStatus.FreeAgent, regen.ContractStatus);
        Assert.InRange(regen.PotentialOverall.GetValueOrDefault(), 80, 96);
        Assert.Contains(result.TransferMarketState.Inbox, notification =>
            notification.Message.Contains("free agent", StringComparison.OrdinalIgnoreCase) &&
            notification.Message.Contains("regen", StringComparison.OrdinalIgnoreCase));
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

    [Fact]
    public void ReplenishAiClubRosters_FillsDepletedClubWithNamedFreeAgents()
    {
        var selectedTeam = new Team
        {
            Name = "Chelsea",
            Players = [new Player { PlayerId = "chelsea-player", Name = "Chelsea Player" }]
        };
        var juventus = new Team
        {
            Name = "Juventus",
            Players =
            [
                new Player { PlayerId = "juve-gk", Name = "Juventus Goalkeeper", Position = Position.Goalkeeper },
                new Player { PlayerId = "juve-df", Name = "Juventus Defender", Position = Position.Defender },
                new Player { PlayerId = "juve-mf", Name = "Juventus Midfielder", Position = Position.Midfielder },
                new Player { PlayerId = "juve-fw", Name = "Juventus Forward", Position = Position.Forward },
                new Player { PlayerId = "juve-fw2", Name = "Juventus Forward 2", Position = Position.Forward }
            ]
        };
        var freeAgents = Enumerable.Range(1, 20)
            .Select(index => new Player
            {
                PlayerId = $"free-agent-{index}",
                Name = $"Named Free Agent {index}",
                Position = (index % 4) switch
                {
                    0 => Position.Goalkeeper,
                    1 => Position.Defender,
                    2 => Position.Midfielder,
                    _ => Position.Forward
                },
                OverallRating = 70 + index,
                ContractStatus = PlayerContractStatus.FreeAgent
            })
            .ToList();
        var state = new TransferMarketState
        {
            Leagues =
            [
                new TransferLeagueState
                {
                    LeagueId = "serie-a",
                    LeagueName = "Serie A",
                    Teams = [juventus]
                },
                new TransferLeagueState
                {
                    LeagueId = "premier-league",
                    LeagueName = "Premier League",
                    Teams = [selectedTeam]
                }
            ],
            FreeAgents = freeAgents
        };

        SeasonRolloverService.ReplenishAiClubRosters(state, selectedTeam, 2031);

        var restoredRoster = juventus.Players.Concat(juventus.Substitutes).ToList();
        Assert.Equal(18, restoredRoster.Count);
        Assert.DoesNotContain(restoredRoster, player => player.Name.Contains("Emergency Player"));
        Assert.All(restoredRoster.Skip(5), player =>
        {
            Assert.True(player.ContractEndYear > 2031);
            Assert.Equal(PlayerContractStatus.Active, player.ContractStatus);
            Assert.StartsWith("serie-a:", player.ClubId);
        });
        Assert.Single(selectedTeam.Players);
        Assert.Equal(7, state.FreeAgents.Count);
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

    private static (League League, Team SelectedTeam) CreateCompletedRolloverContext()
    {
        var leagueEngine = new LeagueEngine();
        var dataService = new LeagueDataService();
        var definition = dataService.GetLeagueDefinition("premier-league");
        var teams = dataService.LoadTeams(definition).Take(6).ToList();
        var selectedTeam = teams[0];
        var league = leagueEngine.CreateLeague("premier-league", GameSessionService.PremierLeagueName, "2025-26", teams);
        league.Table = teams
            .Select((team, index) => new LeagueTableEntry
            {
                TeamName = team.Name,
                Played = 10,
                Wins = Math.Max(0, 5 - index),
                Draws = 0,
                Losses = index,
                GoalsFor = 30 - index,
                GoalsAgainst = 10 + index,
                Points = (6 - index) * 3
            })
            .ToList();
        foreach (var fixture in league.Fixtures)
        {
            fixture.IsPlayed = true;
        }

        return (league, selectedTeam);
    }
}
