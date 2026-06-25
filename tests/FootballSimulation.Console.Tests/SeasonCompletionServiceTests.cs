using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class SeasonCompletionServiceTests
{
    [Fact]
    public void IsSelectedTeamSeasonComplete_IgnoresUnplayedFixturesForOtherTeams()
    {
        var selectedTeam = new Team { Name = "Chelsea" };
        var otherTeam = new Team { Name = "Liverpool" };
        var thirdTeam = new Team { Name = "Arsenal" };
        var fourthTeam = new Team { Name = "Manchester City" };
        var league = new League
        {
            Teams = [selectedTeam, otherTeam, thirdTeam, fourthTeam],
            Fixtures =
            [
                new Fixture
                {
                    HomeTeam = selectedTeam,
                    AwayTeam = otherTeam,
                    IsPlayed = true
                },
                new Fixture
                {
                    HomeTeam = thirdTeam,
                    AwayTeam = fourthTeam,
                    IsPlayed = false
                }
            ]
        };

        var service = new SeasonCompletionService();

        Assert.True(service.IsSelectedTeamSeasonComplete(league, selectedTeam));
        Assert.False(service.IsLeagueComplete(league));
    }
}
