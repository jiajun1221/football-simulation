using FootballSimulation.Data;
using FootballSimulation.Engine;
using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class SaveGameServiceTests
{
    [Fact]
    public void SaveGame_AndLoadGame_RestoresLeagueProgressAndReferences()
    {
        var saveDirectory = CreateTempSaveDirectory();
        var saveGameService = new SaveGameService(saveDirectory);
        var leagueEngine = new LeagueEngine();
        var teams = new LeagueSeedDataService().CreateLeagueTeams();
        var selectedTeam = teams[0];
        var league = leagueEngine.CreateLeague(GameSessionService.PremierLeagueName, teams);
        var fixture = league.Fixtures
            .OrderBy(leagueFixture => leagueFixture.RoundNumber)
            .First(leagueFixture => leagueFixture.HomeTeam == selectedTeam || leagueFixture.AwayTeam == selectedTeam);

        leagueEngine.SimulateFixture(league, fixture);
        selectedTeam.Players[0].GrowthPoints = 42;
        selectedTeam.Players[0].Stamina = 73.25;
        selectedTeam.Players[0].IsInjured = true;
        selectedTeam.Players[0].InjuryType = "Hamstring Injury";
        selectedTeam.Players[0].InjuryRecoveryMatches = 3;
        selectedTeam.Players[1].SuspendedMatches = 1;
        var injuredPlayerName = selectedTeam.Players[0].Name;
        var suspendedPlayerName = selectedTeam.Players[1].Name;

        try
        {
            var saveData = SaveGameService.CreateSaveData(league, selectedTeam);
            saveGameService.SaveGame(1, saveData);

            var loadedData = saveGameService.LoadGame(1);
            var loadedLeague = SaveGameService.CreateLeague(loadedData!);
            var loadedSelectedTeam = loadedLeague.Teams.Single(team => team.Name == selectedTeam.Name);
            var loadedFixture = loadedLeague.Fixtures.Single(savedFixture =>
                savedFixture.RoundNumber == fixture.RoundNumber &&
                savedFixture.HomeTeam.Name == fixture.HomeTeam.Name &&
                savedFixture.AwayTeam.Name == fixture.AwayTeam.Name);
            var loadedSelectedFixture = loadedLeague.Fixtures.First(savedFixture =>
                savedFixture.HomeTeam == loadedSelectedTeam || savedFixture.AwayTeam == loadedSelectedTeam);
            var loadedPlayer = loadedSelectedTeam.Players
                .Concat(loadedSelectedTeam.Substitutes)
                .Single(player => player.Name == injuredPlayerName);

            Assert.NotNull(loadedData);
            Assert.Equal(SaveGameService.CurrentSaveVersion, loadedData!.SaveVersion);
            Assert.Equal(selectedTeam.Name, loadedData.SelectedClubName);
            Assert.True(loadedFixture.IsPlayed);
            Assert.NotNull(loadedFixture.Result);
            Assert.Same(loadedFixture.HomeTeam, loadedFixture.Result!.HomeTeam);
            Assert.Same(loadedFixture.AwayTeam, loadedFixture.Result.AwayTeam);
            Assert.True(loadedSelectedFixture.HomeTeam == loadedSelectedTeam || loadedSelectedFixture.AwayTeam == loadedSelectedTeam);
            Assert.Equal(42, loadedPlayer.GrowthPoints);
            Assert.Equal(73.25, loadedPlayer.Stamina, precision: 2);
            Assert.True(loadedPlayer.IsInjured);
            Assert.Equal("Hamstring Injury", loadedPlayer.InjuryType);
            Assert.Equal(3, loadedPlayer.InjuryRecoveryMatches);
            var suspendedPlayer = loadedSelectedTeam.Players
                .Concat(loadedSelectedTeam.Substitutes)
                .Single(player => player.Name == suspendedPlayerName);
            Assert.Equal(1, suspendedPlayer.SuspendedMatches);
            Assert.True(suspendedPlayer.IsSuspended);
            Assert.Equal(league.Table.Sum(entry => entry.Points), loadedLeague.Table.Sum(entry => entry.Points));
            Assert.Equal(league.Fixtures.Count(fixtureItem => fixtureItem.IsPlayed), loadedData.MatchHistory.Count);
        }
        finally
        {
            DeleteDirectory(saveDirectory);
        }
    }

    [Fact]
    public void CreateLeague_BackfillsMissingPlayerDataFromLeagueData()
    {
        var dataService = new LeagueDataService();
        var definition = dataService.GetLeagueDefinition("premier-league");
        var teams = dataService.LoadTeams(definition);
        var league = new GameSessionService().CreateLeague(definition, teams);
        var selectedTeam = league.Teams.Single(team => team.Name == "Chelsea");
        var expectedAges = selectedTeam.Players.Concat(selectedTeam.Substitutes)
            .ToDictionary(player => player.Name, player => player.Age);
        var expectedFlagPaths = selectedTeam.Players.Concat(selectedTeam.Substitutes)
            .ToDictionary(player => player.Name, player => player.FlagImagePath);
        foreach (var player in league.Teams.SelectMany(team => team.Players.Concat(team.Substitutes)))
        {
            player.Age = null;
            player.NationalityCode = string.Empty;
            player.NationalityName = string.Empty;
            player.Nationality = string.Empty;
            player.FlagImagePath = string.Empty;
        }

        var saveData = SaveGameService.CreateSaveData(league, selectedTeam);
        var restoredLeague = SaveGameService.CreateLeague(saveData);
        var restoredChelsea = restoredLeague.Teams.Single(team => team.Name == "Chelsea");

        Assert.All(restoredLeague.Teams.SelectMany(team => team.Players.Concat(team.Substitutes)), player =>
        {
            Assert.NotNull(player.Age);
            Assert.InRange(player.Age!.Value, 15, 45);
        });
        foreach (var player in restoredChelsea.Players.Concat(restoredChelsea.Substitutes))
        {
            Assert.Equal(expectedAges[player.Name], player.Age);
            Assert.Equal(expectedFlagPaths[player.Name], player.FlagImagePath);
        }
    }

    [Fact]
    public void SaveGame_AndLoadGame_PreservesCustomLineupSlotsAndBenchOrder()
    {
        var saveDirectory = CreateTempSaveDirectory();
        var saveGameService = new SaveGameService(saveDirectory);
        var leagueEngine = new LeagueEngine();
        var teams = new LeagueSeedDataService().CreateLeagueTeams();
        var selectedTeam = teams[0];
        var league = leagueEngine.CreateLeague(GameSessionService.PremierLeagueName, teams);
        var originalPlayers = selectedTeam.Players.Concat(selectedTeam.Substitutes).ToList();
        var goalkeeper = originalPlayers.First(PositionSuitabilityService.IsGoalkeeperCapable);
        var outfield = originalPlayers.Where(player => !PositionSuitabilityService.IsGoalkeeperCapable(player)).Take(10).ToList();
        var slots = new[] { "GK", "RB", "CB", "CB", "LB", "CDM", "CDM", "LW", "CAM", "RW", "ST" };
        var starters = new[] { goalkeeper, outfield[3], outfield[2], outfield[1], outfield[0], outfield[4], outfield[5], outfield[6], outfield[7], outfield[8], outfield[9] };
        var bench = originalPlayers.Except(starters).Take(3).ToArray();

        selectedTeam.Formation = "4-2-3-1";
        selectedTeam.Players = starters.ToList();
        selectedTeam.Substitutes = bench.Concat(originalPlayers.Except(starters).Except(bench)).ToList();
        for (var index = 0; index < selectedTeam.Players.Count; index++)
        {
            PositionSuitabilityService.EnsurePositionMetadata(selectedTeam.Players[index], slots[index]);
        }

        try
        {
            saveGameService.SaveGame(1, SaveGameService.CreateSaveData(league, selectedTeam));

            var loadedData = saveGameService.LoadGame(1);
            var loadedLeague = SaveGameService.CreateLeague(loadedData!);
            var loadedSelectedTeam = loadedLeague.Teams.Single(team => team.Name == selectedTeam.Name);

            Assert.Equal("4-2-3-1", loadedSelectedTeam.Formation);
            Assert.Equal(starters.Select(player => player.Name), loadedSelectedTeam.Players.Select(player => player.Name));
            Assert.Equal(slots, loadedSelectedTeam.Players.Select(player => player.AssignedPosition));
            Assert.Equal(bench.Select(player => player.Name), loadedSelectedTeam.Substitutes.Take(bench.Length).Select(player => player.Name));
        }
        finally
        {
            DeleteDirectory(saveDirectory);
        }
    }

    [Fact]
    public void ClubMatchSetupService_ReplacesOnlyUnavailableStarterSlot()
    {
        var team = new LeagueSeedDataService().CreateLeagueTeams()[0];
        var allPlayers = team.Players.Concat(team.Substitutes).ToList();
        var goalkeeper = allPlayers.First(PositionSuitabilityService.IsGoalkeeperCapable);
        var outfield = allPlayers.Where(player => !PositionSuitabilityService.IsGoalkeeperCapable(player)).Take(13).ToList();
        var slots = new[] { "GK", "RB", "CB", "CB", "LB", "CDM", "CDM", "LW", "CAM", "RW", "ST" };
        var starters = new[] { goalkeeper, outfield[0], outfield[1], outfield[2], outfield[3], outfield[4], outfield[5], outfield[6], outfield[7], outfield[8], outfield[9] };
        var expectedCbName = starters[2].Name;
        var suspendedRightBack = starters[1];

        team.Formation = "4-2-3-1";
        team.Players = starters.ToList();
        team.Substitutes = outfield.Skip(10).Concat(allPlayers.Except(starters).Except(outfield.Skip(10))).ToList();
        for (var index = 0; index < team.Players.Count; index++)
        {
            PositionSuitabilityService.EnsurePositionMetadata(team.Players[index], slots[index]);
        }

        var setup = ClubMatchSetupService.Capture(team);
        suspendedRightBack.SuspendedMatches = 1;

        ClubMatchSetupService.Apply(team, setup);

        Assert.NotEqual(suspendedRightBack.Name, team.Players[1].Name);
        Assert.Equal("RB", team.Players[1].AssignedPosition);
        Assert.Equal(expectedCbName, team.Players[2].Name);
        Assert.Equal("CB", team.Players[2].AssignedPosition);
        Assert.Contains(suspendedRightBack, team.Substitutes);
    }

    [Fact]
    public void GetSaveSlots_ReturnsSavedSlotSummary()
    {
        var saveDirectory = CreateTempSaveDirectory();
        var saveGameService = new SaveGameService(saveDirectory);
        var gameSessionService = new GameSessionService();
        var teams = new LeagueSeedDataService().CreateLeagueTeams();
        var selectedTeam = teams[0];
        var league = gameSessionService.CreatePremierLeague(teams);

        try
        {
            saveGameService.SaveGame(2, SaveGameService.CreateSaveData(league, selectedTeam));

            var slots = saveGameService.GetSaveSlots();
            var savedSlot = slots.Single(slot => slot.SlotNumber == 2);

            Assert.True(slots.Single(slot => slot.SlotNumber == 1).IsEmpty);
            Assert.False(savedSlot.IsEmpty);
            Assert.Equal(selectedTeam.Name, savedSlot.SelectedClubName);
            Assert.Equal(1, savedSlot.CurrentRound);
            Assert.Equal(0, savedSlot.Points);
            Assert.Equal(1, savedSlot.LeaguePosition);
            Assert.NotNull(savedSlot.SavedAt);
        }
        finally
        {
            DeleteDirectory(saveDirectory);
        }
    }

    [Fact]
    public void DeleteSave_RemovesSlotFile()
    {
        var saveDirectory = CreateTempSaveDirectory();
        var saveGameService = new SaveGameService(saveDirectory);
        var gameSessionService = new GameSessionService();
        var teams = new LeagueSeedDataService().CreateLeagueTeams();
        var league = gameSessionService.CreatePremierLeague(teams);

        try
        {
            saveGameService.SaveGame(1, SaveGameService.CreateSaveData(league, teams[0]));
            saveGameService.DeleteSave(1);

            Assert.Null(saveGameService.LoadGame(1));
            Assert.True(saveGameService.GetSaveSlots().Single(slot => slot.SlotNumber == 1).IsEmpty);
        }
        finally
        {
            DeleteDirectory(saveDirectory);
        }
    }

    [Fact]
    public void GetSaveSlots_MarksCorruptedSave()
    {
        var saveDirectory = CreateTempSaveDirectory();
        var saveGameService = new SaveGameService(saveDirectory);

        try
        {
            Directory.CreateDirectory(saveDirectory);
            File.WriteAllText(saveGameService.GetSlotPath(3), "{ not valid json");

            var slot = saveGameService.GetSaveSlots().Single(saveSlot => saveSlot.SlotNumber == 3);

            Assert.True(slot.IsCorrupted);
            Assert.False(slot.IsEmpty);
            Assert.Throws<System.Text.Json.JsonException>(() => saveGameService.LoadGame(3));
        }
        finally
        {
            DeleteDirectory(saveDirectory);
        }
    }

    [Fact]
    public void SaveGame_RejectsInvalidSlotNumber()
    {
        var saveGameService = new SaveGameService(CreateTempSaveDirectory());

        Assert.Throws<ArgumentOutOfRangeException>(() => saveGameService.GetSlotPath(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => saveGameService.GetSlotPath(4));
    }

    private static string CreateTempSaveDirectory()
    {
        return Path.Combine(Path.GetTempPath(), $"football-save-tests-{Guid.NewGuid():N}");
    }

    private static void DeleteDirectory(string directoryPath)
    {
        if (Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }
}
