using FootballSimulation.Data;

namespace FootballSimulation.Console.Tests;

public class SeedDataAgeTests
{
    [Fact]
    public void DemoTeams_AssignRealisticPlayerAges()
    {
        var seedData = new SeedDataService();
        var (homeTeam, awayTeam) = seedData.CreateDemoTeams();

        Assert.All(homeTeam.Players.Concat(awayTeam.Players), player =>
        {
            Assert.NotNull(player.Age);
            Assert.InRange(player.Age!.Value, 15, 45);
        });
    }

    [Fact]
    public void LeagueSeedTeams_AssignRealisticPlayerAges()
    {
        var teams = new LeagueSeedDataService().CreateLeagueTeams();

        Assert.All(teams.SelectMany(team => team.Players), player =>
        {
            Assert.NotNull(player.Age);
            Assert.InRange(player.Age!.Value, 15, 45);
        });
    }
}
