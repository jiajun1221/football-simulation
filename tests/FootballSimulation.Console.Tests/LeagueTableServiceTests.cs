using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class LeagueTableServiceTests
{
    [Fact]
    public void ApplyMatchResult_WinUpdatesPointsAndRecord()
    {
        var leagueTableService = new LeagueTableService();
        var homeTeam = new Team { Name = "Blue Hawks" };
        var awayTeam = new Team { Name = "Red Lions" };
        var table = leagueTableService.CreateTable([homeTeam, awayTeam]);
        var match = new Match
        {
            HomeTeam = homeTeam,
            AwayTeam = awayTeam,
            HomeScore = 2,
            AwayScore = 1
        };

        leagueTableService.ApplyMatchResult(table, match);

        var homeEntry = table.Single(entry => entry.TeamName == homeTeam.Name);
        var awayEntry = table.Single(entry => entry.TeamName == awayTeam.Name);

        Assert.Equal(3, homeEntry.Points);
        Assert.Equal(1, homeEntry.Wins);
        Assert.Equal(0, awayEntry.Points);
        Assert.Equal(1, awayEntry.Losses);
        Assert.Equal(2, homeEntry.GoalsFor);
        Assert.Equal(1, awayEntry.GoalsFor);
    }

    [Fact]
    public void ApplyMatchResult_DrawGivesEachTeamOnePoint()
    {
        var leagueTableService = new LeagueTableService();
        var homeTeam = new Team { Name = "Green Falcons" };
        var awayTeam = new Team { Name = "Golden Bears" };
        var table = leagueTableService.CreateTable([homeTeam, awayTeam]);
        var match = new Match
        {
            HomeTeam = homeTeam,
            AwayTeam = awayTeam,
            HomeScore = 1,
            AwayScore = 1
        };

        leagueTableService.ApplyMatchResult(table, match);

        var homeEntry = table.Single(entry => entry.TeamName == homeTeam.Name);
        var awayEntry = table.Single(entry => entry.TeamName == awayTeam.Name);

        Assert.Equal(1, homeEntry.Points);
        Assert.Equal(1, awayEntry.Points);
        Assert.Equal(1, homeEntry.Draws);
        Assert.Equal(1, awayEntry.Draws);
        Assert.Equal(1, homeEntry.Played);
        Assert.Equal(1, awayEntry.Played);
    }
}
