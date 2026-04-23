using FootballSimulation.Data;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class TeamJsonPersistenceServiceTests
{
    [Fact]
    public void SaveTeams_AndLoadTeams_RoundTripsTeamData()
    {
        var seedDataService = new SeedDataService();
        var persistenceService = new TeamJsonPersistenceService();
        var (homeTeam, awayTeam) = seedDataService.CreateDemoTeams();
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-teams.json");

        try
        {
            persistenceService.SaveTeams(filePath, homeTeam, awayTeam);
            var loadedTeams = persistenceService.LoadTeams(filePath);

            Assert.Equal(homeTeam.Name, loadedTeams.HomeTeam.Name);
            Assert.Equal(homeTeam.Formation, loadedTeams.HomeTeam.Formation);
            Assert.Equal(11, loadedTeams.HomeTeam.Players.Count);

            Assert.Equal(awayTeam.Name, loadedTeams.AwayTeam.Name);
            Assert.Equal(awayTeam.Formation, loadedTeams.AwayTeam.Formation);
            Assert.Equal(11, loadedTeams.AwayTeam.Players.Count);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }
}
