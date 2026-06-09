using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class SeasonAwardsServiceTests
{
    [Fact]
    public void CreateAwards_RequiresMeaningfulAppearancesForMajorAwards()
    {
        var league = new League
        {
            Teams =
            [
                new Team
                {
                    Name = "Chelsea",
                    Players =
                    [
                        new Player { PlayerId = "palmer", Name = "Cole Palmer", Age = 23, Position = Position.Midfielder },
                        new Player { PlayerId = "isidor", Name = "Wilson Isidor", Age = 24, Position = Position.Forward },
                        new Player { PlayerId = "young-low", Name = "Young Prospect", Age = 20, Position = Position.Forward }
                    ]
                }
            ]
        };
        var stats = new List<ArchivedPlayerStatRow>
        {
            new()
            {
                PlayerId = "isidor",
                PlayerName = "Wilson Isidor",
                TeamName = "Chelsea",
                Position = Position.Forward,
                ExactPosition = "ST",
                Appearances = 2,
                Goals = 4,
                Assists = 1,
                AverageRating = 10.00
            },
            new()
            {
                PlayerId = "young-low",
                PlayerName = "Young Prospect",
                TeamName = "Chelsea",
                Position = Position.Forward,
                ExactPosition = "ST",
                Appearances = 4,
                Goals = 8,
                Assists = 2,
                AverageRating = 9.50
            },
            new()
            {
                PlayerId = "palmer",
                PlayerName = "Cole Palmer",
                TeamName = "Chelsea",
                Position = Position.Midfielder,
                ExactPosition = "CAM",
                Appearances = 35,
                Goals = 20,
                Assists = 15,
                AverageRating = 8.13
            }
        };

        var awards = new SeasonAwardsService().CreateAwards(league, stats);

        Assert.Equal("Cole Palmer", awards.PlayerOfTheSeason.PlayerName);
        Assert.Equal("Cole Palmer", awards.YoungPlayerOfTheSeason.PlayerName);
    }
}
