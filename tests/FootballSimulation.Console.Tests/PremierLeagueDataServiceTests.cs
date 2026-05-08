using System.Text.Json;
using FootballSimulation.Data.JsonModels;
using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class PremierLeagueDataServiceTests
{
    [Fact]
    public void TeamsJson_ContainsAllPremierLeagueTeams()
    {
        var teamsFilePath = Path.Combine(
            AppContext.BaseDirectory,
            "Data",
            "Json",
            "premier-league-2025-26-teams.json");
        var json = File.ReadAllText(teamsFilePath);
        var teamsFile = JsonSerializer.Deserialize<PremierLeagueTeamsFile>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(teamsFile);
        Assert.Equal(20, teamsFile.Teams.Count);
    }

    [Fact]
    public void LoadTeams_ReturnsPlayableTeamsFromJson()
    {
        var dataService = new PremierLeagueDataService();

        var teams = dataService.LoadTeams();

        Assert.Equal(20, teams.Count);
        Assert.All(teams, team =>
        {
            Assert.False(string.IsNullOrWhiteSpace(team.Name));
            Assert.False(string.IsNullOrWhiteSpace(team.Formation));
            Assert.Equal(11, team.Players.Count);
            Assert.NotNull(team.Substitutes);
            Assert.InRange(team.Substitutes.Count, 7, 12);
            Assert.Contains(team.Players, player => player.Position == Position.Goalkeeper);
            Assert.Contains(team.Substitutes, player => player.Position == Position.Goalkeeper);
            Assert.All(team.Players, player => Assert.True(player.IsStarter));
            Assert.All(team.Substitutes, player => Assert.False(player.IsStarter));
        });
    }

    [Fact]
    public void LoadTeams_MapsPlayerRatingsIntoGameStats()
    {
        var dataService = new PremierLeagueDataService();

        var teams = dataService.LoadTeams();
        var firstPlayer = teams.SelectMany(team => team.Players).First();

        Assert.InRange(firstPlayer.Attack, 1, 100);
        Assert.InRange(firstPlayer.Defense, 1, 100);
        Assert.InRange(firstPlayer.Passing, 1, 100);
        Assert.InRange(firstPlayer.Stamina, 1, 100);
        Assert.InRange(firstPlayer.Finishing, 1, 100);
        Assert.InRange(firstPlayer.OverallRating, 1, 100);
        Assert.InRange(firstPlayer.CurrentForm, 1, 100);
        Assert.False(string.IsNullOrWhiteSpace(firstPlayer.Form));
        Assert.InRange(firstPlayer.CurrentStamina, 0, firstPlayer.Stamina);
    }

    [Fact]
    public void LoadTeams_ResetsLoadedFormToAverageForNewSession()
    {
        var dataService = new PremierLeagueDataService();

        var teams = dataService.LoadTeams();
        var players = teams.SelectMany(team => team.Players.Concat(team.Substitutes));

        Assert.All(players, player =>
        {
            Assert.Equal(PlayerFormStatus.Average, player.FormStatus);
            Assert.Equal("Average", player.Form);
            Assert.Equal(50, player.CurrentForm);
        });
    }

    [Theory]
    [InlineData(9.2, PlayerFormStatus.Excellent)]
    [InlineData(8.0, PlayerFormStatus.Good)]
    [InlineData(7.0, PlayerFormStatus.Average)]
    [InlineData(5.9, PlayerFormStatus.Poor)]
    [InlineData(5.2, PlayerFormStatus.Poor)]
    [InlineData(4.9, PlayerFormStatus.VeryPoor)]
    public void CalculateFormStatus_UsesLiveThresholds(double rating, PlayerFormStatus expectedStatus)
    {
        var player = new Player
        {
            Name = "Test Player",
            Stamina = 100
        };

        var performance = new PlayerMatchPerformance
        {
            PlayerName = player.Name,
            TeamName = "Chelsea",
            Rating = rating
        };

        var status = PlayerFormStatusService.CalculateFormStatus(player, performance);

        Assert.Equal(expectedStatus, status);
    }
}
