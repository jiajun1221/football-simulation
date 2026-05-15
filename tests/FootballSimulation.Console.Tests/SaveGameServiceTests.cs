using FootballSimulation.Data;
using FootballSimulation.Engine;
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
            var loadedPlayer = loadedSelectedTeam.Players[0];

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
            Assert.Equal(1, loadedSelectedTeam.Players[1].SuspendedMatches);
            Assert.True(loadedSelectedTeam.Players[1].IsSuspended);
            Assert.Equal(league.Table.Sum(entry => entry.Points), loadedLeague.Table.Sum(entry => entry.Points));
            Assert.Equal(league.Fixtures.Count(fixtureItem => fixtureItem.IsPlayed), loadedData.MatchHistory.Count);
        }
        finally
        {
            DeleteDirectory(saveDirectory);
        }
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
